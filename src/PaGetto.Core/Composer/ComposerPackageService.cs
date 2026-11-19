using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.MaliciousPackages;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Composer;

public partial class ComposerPackageService : IComposerPackageService
{
    private const long BytesPerMiB = 1024L * 1024;
    private const int MaxDescriptionLength = 4000;

    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly ComposerUpstreamClient _upstream;
    private readonly PackageMirrorLock _mirrorLock;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly SystemTime _time;
    private readonly MaliciousPackageGuard _malicious;
    private readonly ILogger<ComposerPackageService> _logger;

    public ComposerPackageService(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        ComposerUpstreamClient upstream,
        PackageMirrorLock mirrorLock,
        IFeedSettingsResolver feedSettings,
        SystemTime time,
        MaliciousPackageGuard malicious,
        ILogger<ComposerPackageService> logger)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _mirrorLock = mirrorLock ?? throw new ArgumentNullException(nameof(mirrorLock));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _malicious = malicious ?? throw new ArgumentNullException(nameof(malicious));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> GetMetadataOrNullAsync(Feed feed, string name, bool dev, string distBaseUrl, CancellationToken cancellationToken)
    {
        if (!ComposerPackageName.IsValid(name))
        {
            return null;
        }

        name = name.ToLowerInvariant();
        var local = await _artifacts.FindVersionsAsync(feed.Id, name, cancellationToken);
        var upstream = await _upstream.ListVersionsAsync(feed, name, dev, cancellationToken);

        var versions = new List<(string Normalized, JsonObject Package)>();

        // Every version of the feed, listed or not, hides the mirror's version of the same normalized form.
        var stored = new HashSet<string>(StringComparer.Ordinal);
        foreach (var version in local)
        {
            if (!ComposerVersion.TryParse(version.Version, out var parsed))
            {
                continue;
            }

            stored.Add(parsed.Normalized);
            if (version.Listed && parsed.IsDev == dev
                && ComposerManifest.ToPackage(version.MetadataJson, distBaseUrl, name, version.Version) is { } package)
            {
                versions.Add((parsed.Normalized, package));
            }
        }

        foreach (var version in upstream)
        {
            if (stored.Contains(version.NormalizedVersion) || JsonNode.Parse(version.Json) is not JsonObject package)
            {
                continue;
            }

            // A tagged version is downloaded through this feed, which stores it. The mirror's reference
            // and SHA-1 are kept, since the zip is stored as the mirror serves it.
            if (!dev && ComposerVersion.IsStorable(version.Version) && ComposerUpstreamClient.TryGetZipDist(package, out _, out _))
            {
                package["dist"]!["url"] = $"{distBaseUrl}/{ComposerManifest.GetDistPath(name, version.Version, ComposerManifest.GetDistReference(package))}";
            }

            versions.Add((version.NormalizedVersion, package));
        }

        if (versions.Count == 0)
        {
            return null;
        }

        // Newest first, as Packagist lists them.
        var sorted = versions
            .OrderByDescending(v => v.Normalized, Comparer<string>.Create(ComposerVersion.CompareNormalized))
            .Select(v => (JsonNode)v.Package)
            .ToArray();

        return new JsonObject
        {
            ["packages"] = new JsonObject { [name] = new JsonArray(sorted) },
        }.ToJsonString();
    }

    public async Task<Stream> GetDistStreamOrNullAsync(Feed feed, string name, string version, CancellationToken cancellationToken)
    {
        if (!ComposerPackageName.IsValid(name) || !ComposerVersion.TryParse(version, out var parsed))
        {
            return null;
        }

        name = name.ToLowerInvariant();

        // Development versions of mirrors change, so they are not stored.
        var stored = await FindStoredVersionOrNullAsync(feed, name, parsed, cancellationToken);
        await _malicious.EnsureAllowedAsync(feed, name, stored?.Version ?? version, stored, cancellationToken);
        stored ??= parsed.IsDev ? null : await MirrorAsync(feed, name, parsed, cancellationToken);
        if (stored == null)
        {
            return null;
        }

        await _artifacts.AddDownloadAsync(feed.Id, name, stored.Version, cancellationToken);

        return await _storage.GetArchiveStreamAsync(feed.Slug, name, stored.Version, cancellationToken);
    }

    public async Task<ComposerUploadResult> UploadAsync(Feed feed, string name, string version, Stream zip, long maxBytes, CancellationToken cancellationToken)
    {
        if (!ComposerPackageName.IsValid(name))
        {
            return Invalid(
                $"{name} is not a valid package name: vendor/package, of letters and digits separated by '_', '.' or '-', " +
                $"and at most {ComposerPackageName.MaxLength} characters.");
        }

        if (!ComposerVersion.IsStorable(version) || !ComposerVersion.TryParse(version, out var parsed))
        {
            return Invalid($"{version} is not a version Composer accepts, such as 1.0.0, v1.2.3-beta1 or dev-main.");
        }

        name = name.ToLowerInvariant();

        ArtifactFileContent content;
        try
        {
            content = await ArtifactFileContent.ReadAsync(zip, maxBytes, cancellationToken);
        }
        catch (ArtifactFileTooLargeException)
        {
            return new ComposerUploadResult
            {
                Status = ComposerUploadStatus.TooLarge,
                Message = $"The zip is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.",
            };
        }

        using (content)
        {
            ComposerArchive archive;
            try
            {
                archive = ComposerArchiveReader.Read(content.Stream);
            }
            catch (InvalidComposerPackageException e)
            {
                LogInvalidPackage(e, feed.Slug);
                return Invalid(e.Message);
            }

            var declaredName = GetString(archive.ComposerJson, "name");
            if (!string.Equals(declaredName, name, StringComparison.OrdinalIgnoreCase))
            {
                return Invalid($"The composer.json file names the package {declaredName ?? "(none)"}, not {name}.");
            }

            var declaredVersion = GetString(archive.ComposerJson, "version");
            if (declaredVersion != null
                && (!ComposerVersion.TryParse(declaredVersion, out var declared) || declared.Normalized != parsed.Normalized))
            {
                return Invalid($"The composer.json file has the version {declaredVersion}, not {version}.");
            }

            var existing = await FindStoredVersionOrNullAsync(feed, name, parsed, cancellationToken);
            if (existing != null)
            {
                // Development versions follow a branch, so they can always be replaced, like Maven snapshots.
                if (!parsed.IsDev && !_feedSettings.CanOverwrite(feed, parsed.IsPrerelease))
                {
                    return AlreadyExists(name, existing.Version);
                }

                await _artifacts.HardDeleteVersionAsync(feed.Id, name, existing.Version, cancellationToken);
                await _storage.DeleteAsync(feed.Slug, name, existing.Version, cancellationToken);
            }

            var result = await IndexAsync(
                feed,
                name,
                parsed,
                ComposerManifest.Create(archive.ComposerJson, name, parsed, _time.UtcNow, content.Sha1),
                GetString(archive.ComposerJson, "description"),
                archive.Readme,
                content,
                cachedFrom: null,
                cancellationToken);

            if (result == ArtifactAddResult.AlreadyExists)
            {
                return AlreadyExists(name, version);
            }
        }

        LogUploaded(name, version, feed.Slug);

        return new ComposerUploadResult { Status = ComposerUploadStatus.Success };
    }

    /// <summary>
    /// The stored version of a normalized form, whichever way it is written.
    /// </summary>
    private async Task<ArtifactVersion> FindStoredVersionOrNullAsync(Feed feed, string name, ComposerVersion version, CancellationToken cancellationToken)
    {
        // Clients ask for the spelling the index gave them, which is the stored one: that is one
        // indexed lookup. Other spellings of the same version are matched against every version.
        var exact = await _artifacts.FindVersionOrNullAsync(feed.Id, name, version.Text, cancellationToken);
        if (exact != null && ComposerVersion.TryParse(exact.Version, out var exactParsed) && exactParsed.Normalized == version.Normalized)
        {
            return exact;
        }

        var versions = await _artifacts.FindVersionsAsync(feed.Id, name, cancellationToken);

        return versions.FirstOrDefault(v => ComposerVersion.TryParse(v.Version, out var parsed) && parsed.Normalized == version.Normalized);
    }

    /// <summary>
    /// Stores a tagged version from the feed's mirrors in the feed, unless it is there already.
    /// </summary>
    /// <returns>The stored version, or null if no mirror has it.</returns>
    private async Task<ArtifactVersion> MirrorAsync(Feed feed, string name, ComposerVersion version, CancellationToken cancellationToken)
    {
        using var mirrorLock = await _mirrorLock.AcquireAsync(feed.Id, name, version.Normalized, cancellationToken);

        // A concurrent request may have mirrored the version while we waited for the lock.
        var stored = await FindStoredVersionOrNullAsync(feed, name, version, cancellationToken);
        if (stored != null)
        {
            return stored;
        }

        try
        {
            var upstream = (await _upstream.ListVersionsAsync(feed, name, dev: false, cancellationToken))
                .FirstOrDefault(v => v.NormalizedVersion == version.Normalized);
            if (upstream == null
                || !ComposerVersion.TryParse(upstream.Version, out var upstreamVersion)
                || !ComposerVersion.IsStorable(upstream.Version))
            {
                return null;
            }

            var maxBytes = _feedSettings.GetMaxPackageSizeBytes(feed);
            using var zip = await _upstream.DownloadDistOrNullAsync(feed, upstream, maxBytes, cancellationToken);
            if (zip == null)
            {
                return null;
            }

            // The readme is only shown in the web UI, so a mirrored zip that this server can't read is still served.
            string readme = null;
            try
            {
                readme = ComposerArchiveReader.Read(zip.Stream).Readme;
            }
            catch (InvalidComposerPackageException e)
            {
                LogMirroredZipUnreadable(e, name, upstream.Version);
            }

            var metadata = (JsonObject)JsonNode.Parse(upstream.Json);
            var result = await IndexAsync(
                feed,
                name,
                upstreamVersion,
                ComposerManifest.CreateMirrored(metadata, name, zip.Sha1),
                GetString(metadata, "description"),
                readme,
                zip,
                cachedFrom: upstream.Mirror.PackageSource.AbsoluteUri,
                cancellationToken);

            LogMirrored(name, upstream.Version, upstream.Mirror.PackageSource, result);

            // AlreadyExists means another instance stored the version meanwhile, so it is available.
            return await FindStoredVersionOrNullAsync(feed, name, version, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogMirrorFailed(e, name, version.Text);

            return null;
        }
    }

    private async Task<ArtifactAddResult> IndexAsync(
        Feed feed,
        string name,
        ComposerVersion version,
        string manifestJson,
        string description,
        string readme,
        ArtifactFileContent zip,
        string cachedFrom,
        CancellationToken cancellationToken)
    {
        description = description?.Trim();

        return await ArtifactPublisher.AddAsync(
            _artifacts,
            _storage,
            feed,
            name,
            new ArtifactVersion
            {
                Version = version.Text,
                IsPrerelease = version.IsPrerelease,
                Listed = true,
                Description = description?.Length > MaxDescriptionLength ? description[..MaxDescriptionLength] : description,
                MetadataJson = manifestJson,
                ContentHash = zip.Sha256,
                Size = zip.Size,
                CachedFrom = cachedFrom,
                Published = _time.UtcNow,
            },
            zip.Stream,
            readme,
            cancellationToken);
    }

    private static string GetString(JsonObject item, string property)
    {
        return item?[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    private static ComposerUploadResult Invalid(string message)
    {
        return new ComposerUploadResult { Status = ComposerUploadStatus.InvalidPackage, Message = message };
    }

    private static ComposerUploadResult AlreadyExists(string name, string version)
    {
        return new ComposerUploadResult
        {
            Status = ComposerUploadStatus.AlreadyExists,
            Message = $"Version {version} of package {name} already exists.",
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A package zip uploaded to feed {FeedSlug} is invalid")]
    private partial void LogInvalidPackage(Exception exception, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Uploaded package {PackageName} {PackageVersion} to feed {FeedSlug}")]
    private partial void LogUploaded(string packageName, string packageVersion, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mirrored package {PackageName} {PackageVersion} from {PackageSource} with result {Result}")]
    private partial void LogMirrored(string packageName, string packageVersion, Uri packageSource, ArtifactAddResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mirrored package {PackageName} {PackageVersion} could not be read, storing it without a readme")]
    private partial void LogMirroredZipUnreadable(Exception exception, string packageName, string packageVersion);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror package {PackageName} {PackageVersion}")]
    private partial void LogMirrorFailed(Exception exception, string packageName, string packageVersion);
}
