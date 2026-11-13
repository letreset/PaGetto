using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Go;

public partial class GoModuleService : IGoModuleService
{
    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly GoUpstreamClient _upstream;
    private readonly PackageMirrorLock _mirrorLock;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly SystemTime _time;
    private readonly ILogger<GoModuleService> _logger;

    public GoModuleService(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        GoUpstreamClient upstream,
        PackageMirrorLock mirrorLock,
        IFeedSettingsResolver feedSettings,
        SystemTime time,
        ILogger<GoModuleService> logger)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _mirrorLock = mirrorLock ?? throw new ArgumentNullException(nameof(mirrorLock));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<string>> ListVersionsOrNullAsync(Feed feed, string modulePath, CancellationToken cancellationToken)
    {
        if (!GoModulePath.IsValid(modulePath))
        {
            return null;
        }

        var name = GoModulePath.Escape(modulePath);
        var local = await _artifacts.FindVersionsAsync(feed.Id, name, cancellationToken);
        var upstream = await _upstream.ListVersionsAsync(feed, name, cancellationToken);

        var versions = Combine(local, upstream)
            .Where(v => GoVersion.TryParse(v, out var version) && !version.IsPseudo)
            .OrderBy(v => v, GoVersion.StringComparer)
            .ToList();

        // A module with only pseudo-versions has an empty list; the go command then asks for @latest.
        if (local.Count == 0 && upstream.Count == 0
            && await _upstream.GetLatestOrNullAsync(feed, name, cancellationToken) == null)
        {
            return null;
        }

        return versions;
    }

    public async Task<string> GetLatestInfoOrNullAsync(Feed feed, string modulePath, CancellationToken cancellationToken)
    {
        if (!GoModulePath.IsValid(modulePath))
        {
            return null;
        }

        var name = GoModulePath.Escape(modulePath);
        var local = await _artifacts.FindVersionsAsync(feed.Id, name, cancellationToken);
        var candidates = Combine(local, await _upstream.ListVersionsAsync(feed, name, cancellationToken))
            .Select(v => GoVersion.TryParse(v, out var version) ? version : null)
            .Where(v => v != null)
            .ToList();

        var tagged = candidates.Where(v => !v.IsPseudo).ToList();
        var latest = tagged.Where(v => !v.IsPrerelease).Max() ?? tagged.Max();

        GoUpstreamInfo upstreamLatest = null;
        if (latest == null)
        {
            // Without a tagged version, the newest pseudo-version, of the feed or of the mirrors.
            upstreamLatest = await _upstream.GetLatestOrNullAsync(feed, name, cancellationToken);
            if (upstreamLatest != null && !IsUnlisted(local, upstreamLatest.Version) && GoVersion.TryParse(upstreamLatest.Version, out var upstreamVersion))
            {
                candidates.Add(upstreamVersion);
            }

            latest = candidates.Max();
        }

        if (latest == null)
        {
            return null;
        }

        var stored = FindExact(local, latest.ToString());
        if (stored != null)
        {
            return CreateInfo(stored);
        }

        var info = upstreamLatest?.Version == latest.ToString()
            ? upstreamLatest
            : await _upstream.GetInfoOrNullAsync(feed, name, GoModulePath.Escape(latest.ToString()), cancellationToken);

        return info == null ? null : GoModuleManifest.CreateInfo(info.Version, info.Time ?? _time.UtcNow);
    }

    public async Task<string> GetInfoOrNullAsync(Feed feed, string modulePath, string version, CancellationToken cancellationToken)
    {
        if (!GoModulePath.IsValid(modulePath) || !IsValidQuery(version))
        {
            return null;
        }

        var name = GoModulePath.Escape(modulePath);
        if (GoVersion.TryParse(version, out _))
        {
            var stored = await FindStoredVersionOrNullAsync(feed, name, version, cancellationToken);
            if (stored != null)
            {
                return CreateInfo(stored);
            }
        }

        var info = await _upstream.GetInfoOrNullAsync(feed, name, GoModulePath.Escape(version), cancellationToken);

        return info == null ? null : GoModuleManifest.CreateInfo(info.Version, info.Time ?? _time.UtcNow);
    }

    public async Task<string> GetModOrNullAsync(Feed feed, string modulePath, string version, CancellationToken cancellationToken)
    {
        if (!GoModulePath.IsValid(modulePath) || !GoVersion.TryParse(version, out _))
        {
            return null;
        }

        var name = GoModulePath.Escape(modulePath);
        var stored = await FindStoredVersionOrNullAsync(feed, name, version, cancellationToken);
        if (stored != null)
        {
            // Like the go command, a module without a go.mod file gets one that only names it.
            return GoModuleManifest.Parse(stored.MetadataJson).Mod ?? $"module {modulePath}\n";
        }

        return await _upstream.GetModOrNullAsync(feed, name, GoModulePath.Escape(version), cancellationToken);
    }

    public async Task<Stream> GetZipStreamOrNullAsync(Feed feed, string modulePath, string version, CancellationToken cancellationToken)
    {
        if (!GoModulePath.IsValid(modulePath) || !GoVersion.TryParse(version, out var parsedVersion))
        {
            return null;
        }

        var name = GoModulePath.Escape(modulePath);

        if (await FindStoredVersionOrNullAsync(feed, name, version, cancellationToken) == null
            && !await MirrorAsync(feed, modulePath, parsedVersion, cancellationToken))
        {
            return null;
        }

        await _artifacts.AddDownloadAsync(feed.Id, name, version, cancellationToken);

        return await _storage.GetArchiveStreamAsync(feed.Slug, name, version, cancellationToken);
    }

    public async Task<GoUploadResult> UploadAsync(Feed feed, string modulePath, string version, Stream zip, long maxBytes, CancellationToken cancellationToken)
    {
        if (!GoModulePath.IsValid(modulePath))
        {
            return Invalid(
                $"{modulePath} is not a valid module path: path elements of ASCII letters, digits and '-._~', " +
                $"a first element with a dot and without uppercase letters, and at most {GoModulePath.MaxEscapedLength} characters once escaped.");
        }

        if (!GoVersion.TryParse(version, out var parsedVersion))
        {
            return Invalid($"{version} is not a canonical module version, e.g. v1.2.3.");
        }

        if (GoModulePath.CheckMajorVersion(modulePath, parsedVersion) is { } majorVersionError)
        {
            return Invalid(majorVersionError);
        }

        ArtifactFileContent content;
        try
        {
            content = await ArtifactFileContent.ReadAsync(zip, maxBytes, cancellationToken);
        }
        catch (ArtifactFileTooLargeException)
        {
            return new GoUploadResult
            {
                Status = GoUploadStatus.TooLarge,
                Message = $"The zip is larger than the feed's limit of {maxBytes / 1024 / 1024} MiB.",
            };
        }

        try
        {
            GoModuleZip module;
            try
            {
                module = GoModuleZipReader.Read(content.Stream, modulePath, version);
            }
            catch (InvalidGoModuleException e)
            {
                LogInvalidModule(e, feed.Slug);
                return Invalid(e.Message);
            }

            if (module.GoMod == null)
            {
                return Invalid($"The zip has no {modulePath}@{version}/go.mod file.");
            }

            // A zip from git archive has folder entries, which would make go mod verify fail.
            if (module.HasDirectoryEntries)
            {
                var withoutDirectories = await GoModuleZipReader.RemoveDirectoryEntriesAsync(content.Stream, cancellationToken);
                content.Dispose();
                content = withoutDirectories;
            }

            var declared = GoModFile.Parse(module.GoMod).Module;
            if (declared != modulePath)
            {
                return Invalid($"The go.mod file declares the module {declared ?? "(none)"}, not {modulePath}.");
            }

            var name = GoModulePath.Escape(modulePath);
            var existing = await _artifacts.FindVersionOrNullAsync(feed.Id, name, version, cancellationToken);
            if (existing != null)
            {
                var allowOverwrites = _feedSettings.GetAllowPackageOverwrites(feed);
                if (allowOverwrites == PackageOverwriteAllowed.False
                    || (allowOverwrites == PackageOverwriteAllowed.PrereleaseOnly && !parsedVersion.IsPrerelease))
                {
                    return AlreadyExists(modulePath, existing.Version);
                }

                await _artifacts.HardDeleteVersionAsync(feed.Id, name, existing.Version, cancellationToken);
                await _storage.DeleteAsync(feed.Slug, name, existing.Version, cancellationToken);
            }

            var result = await IndexAsync(
                feed,
                name,
                parsedVersion,
                GoModuleManifest.Create(modulePath, _time.UtcNow, module.GoMod),
                module.Readme,
                content,
                cachedFrom: null,
                cancellationToken);

            if (result == ArtifactAddResult.AlreadyExists)
            {
                return AlreadyExists(modulePath, version);
            }
        }
        finally
        {
            content.Dispose();
        }

        LogUploaded(modulePath, version, feed.Slug);

        return new GoUploadResult { Status = GoUploadStatus.Success };
    }

    /// <summary>
    /// The versions of the feed and of its mirrors, without those unlisted in the feed.
    /// </summary>
    private static HashSet<string> Combine(IReadOnlyList<ArtifactVersion> local, IReadOnlyList<string> upstream)
    {
        var versions = new HashSet<string>(StringComparer.Ordinal);
        versions.UnionWith(upstream.Where(v => !IsUnlisted(local, v)));
        versions.UnionWith(local.Where(v => v.Listed).Select(v => v.Version));

        return versions;
    }

    private static bool IsUnlisted(IReadOnlyList<ArtifactVersion> local, string version)
    {
        return FindExact(local, version) is { Listed: false };
    }

    /// <summary>
    /// The stored version of exactly this text. The database matches versions regardless of case,
    /// but Go versions are case-sensitive.
    /// </summary>
    private static ArtifactVersion FindExact(IReadOnlyList<ArtifactVersion> versions, string version)
    {
        return versions.FirstOrDefault(v => string.Equals(v.Version, version, StringComparison.Ordinal));
    }

    private async Task<ArtifactVersion> FindStoredVersionOrNullAsync(Feed feed, string name, string version, CancellationToken cancellationToken)
    {
        var stored = await _artifacts.FindVersionOrNullAsync(feed.Id, name, version, cancellationToken);

        return stored != null && string.Equals(stored.Version, version, StringComparison.Ordinal) ? stored : null;
    }

    private static string CreateInfo(ArtifactVersion version)
    {
        return GoModuleManifest.CreateInfo(version.Version, GoModuleManifest.Parse(version.MetadataJson).Time ?? version.Published);
    }

    /// <summary>
    /// Whether a version query can be passed on to a mirror: a canonical version, or a branch,
    /// tag or commit name of URL-safe characters.
    /// </summary>
    private static bool IsValidQuery(string version)
    {
        if (string.IsNullOrEmpty(version) || version.Length > GoVersion.MaxLength || version[0] == '.')
        {
            return false;
        }

        foreach (var c in version)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '.' or '_' or '~' or '+'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Stores a version from the feed's mirrors in the feed, unless it is there already.
    /// </summary>
    /// <returns>True if the feed has the version afterwards.</returns>
    private async Task<bool> MirrorAsync(Feed feed, string modulePath, GoVersion parsedVersion, CancellationToken cancellationToken)
    {
        var version = parsedVersion.ToString();
        var name = GoModulePath.Escape(modulePath);
        var escapedVersion = GoModulePath.Escape(version);

        using var mirrorLock = await _mirrorLock.AcquireAsync(feed.Id, name, escapedVersion, cancellationToken);

        // A concurrent request may have mirrored the version while we waited for the lock.
        if (await FindStoredVersionOrNullAsync(feed, name, version, cancellationToken) != null)
        {
            return true;
        }

        try
        {
            var info = await _upstream.GetInfoOrNullAsync(feed, name, escapedVersion, cancellationToken);
            if (info?.Version != version)
            {
                return false;
            }

            var mod = await _upstream.GetModOrNullAsync(feed, name, escapedVersion, cancellationToken);
            var download = mod == null ? null : await _upstream.DownloadZipOrNullAsync(feed, name, escapedVersion, cancellationToken);
            if (download is not { } upstreamZip)
            {
                return false;
            }

            using var zip = upstreamZip.Zip;

            // The readme is only shown in the web UI, so a mirrored zip that this server can't read is still served.
            string readme = null;
            try
            {
                readme = GoModuleZipReader.Read(zip.Stream, modulePath, version).Readme;
            }
            catch (InvalidGoModuleException e)
            {
                LogMirroredZipUnreadable(e, modulePath, version);
            }

            var result = await IndexAsync(
                feed,
                name,
                parsedVersion,
                GoModuleManifest.Create(modulePath, info.Time ?? _time.UtcNow, mod),
                readme,
                zip,
                cachedFrom: upstreamZip.Mirror.PackageSource.AbsoluteUri,
                cancellationToken);

            LogMirrored(modulePath, version, upstreamZip.Mirror.PackageSource, result);

            // AlreadyExists means another instance stored the version meanwhile, so it is available.
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogMirrorFailed(e, modulePath, version);

            return false;
        }
    }

    private async Task<ArtifactAddResult> IndexAsync(
        Feed feed,
        string name,
        GoVersion version,
        string manifestJson,
        string readme,
        ArtifactFileContent zip,
        string cachedFrom,
        CancellationToken cancellationToken)
    {
        zip.Stream.Position = 0;
        using var readmeStream = readme == null ? null : new MemoryStream(Encoding.UTF8.GetBytes(readme));

        await _storage.SaveAsync(feed.Slug, name, version.ToString(), zip.Stream, readmeStream, cancellationToken);

        return await _artifacts.AddVersionAsync(
            feed.Id,
            name,
            new ArtifactVersion
            {
                Version = version.ToString(),
                IsPrerelease = version.IsPrerelease,
                Listed = true,
                MetadataJson = manifestJson,
                ContentHash = zip.Sha256,
                Size = zip.Size,
                HasReadme = readme != null,
                CachedFrom = cachedFrom,
                Published = _time.UtcNow,
            },
            cancellationToken);
    }

    private static GoUploadResult Invalid(string message)
    {
        return new GoUploadResult { Status = GoUploadStatus.InvalidModule, Message = message };
    }

    private static GoUploadResult AlreadyExists(string modulePath, string version)
    {
        return new GoUploadResult
        {
            Status = GoUploadStatus.AlreadyExists,
            Message = $"Version {version} of module {modulePath} already exists.",
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A module zip uploaded to feed {FeedSlug} is invalid")]
    private partial void LogInvalidModule(Exception exception, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Uploaded module {PackageName} {PackageVersion} to feed {FeedSlug}")]
    private partial void LogUploaded(string packageName, string packageVersion, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mirrored module {PackageName} {PackageVersion} from {PackageSource} with result {Result}")]
    private partial void LogMirrored(string packageName, string packageVersion, Uri packageSource, ArtifactAddResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mirrored module {PackageName} {PackageVersion} could not be read, storing it without a readme")]
    private partial void LogMirroredZipUnreadable(Exception exception, string packageName, string packageVersion);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror module {PackageName} {PackageVersion}")]
    private partial void LogMirrorFailed(Exception exception, string packageName, string packageVersion);
}
