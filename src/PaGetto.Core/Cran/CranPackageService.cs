using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.MaliciousPackages;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Cran;

public partial class CranPackageService : ICranPackageService
{
    private const long BytesPerMiB = 1024L * 1024;
    private const int MaxDescriptionLength = 4000;

    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly CranUpstreamClient _upstream;
    private readonly PackageMirrorLock _mirrorLock;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly SystemTime _time;
    private readonly MaliciousPackageGuard _malicious;
    private readonly ILogger<CranPackageService> _logger;

    public CranPackageService(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        CranUpstreamClient upstream,
        PackageMirrorLock mirrorLock,
        IFeedSettingsResolver feedSettings,
        SystemTime time,
        MaliciousPackageGuard malicious,
        ILogger<CranPackageService> logger)
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

    public async Task<string> GetIndexAsync(Feed feed, CancellationToken cancellationToken)
    {
        var artifacts = (await _artifacts.SearchAsync(feed.Id, query: null, skip: 0, take: int.MaxValue, cancellationToken)).Artifacts;
        var upstream = await _upstream.GetIndexAsync(feed, cancellationToken);

        var entries = new List<(string Name, IReadOnlyList<KeyValuePair<string, string>> Fields)>();
        foreach (var artifact in artifacts)
        {
            var current = FindCurrent(artifact.Name, artifact.Versions, upstream.GetValueOrDefault(artifact.Name));
            if (current.Fields != null)
            {
                entries.Add(current);
            }
        }

        var stored = artifacts.Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, version) in upstream)
        {
            if (!stored.Contains(key))
            {
                entries.Add((version.Name, version.IndexEntry));
            }
        }

        var index = new StringBuilder();
        foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.Ordinal))
        {
            CranDcf.WriteRecord(index, CranManifest.ToIndexEntry(entry.Fields));
        }

        return index.ToString();
    }

    public async Task<IReadOnlyList<string>> ListArchiveOrNullAsync(Feed feed, string name, CancellationToken cancellationToken)
    {
        if (!CranPackageName.IsValid(name))
        {
            return null;
        }

        var key = CranPackageName.Normalize(name);
        var local = await _artifacts.FindVersionsAsync(feed.Id, key, cancellationToken);
        var upstream = await _upstream.ListVersionsAsync(feed, name, cancellationToken);
        if (local.Count == 0 && upstream.Count == 0)
        {
            return null;
        }

        var current = FindCurrent(name, local, upstream.FirstOrDefault(v => v.IndexEntry != null));
        var currentVersion = CranVersion.TryParse(CranDcf.GetValue(current.Fields ?? [], CranManifest.VersionField), out var parsed)
            ? parsed.Normalized
            : null;

        var files = new Dictionary<string, (CranVersion Version, string FileName)>(StringComparer.Ordinal);
        foreach (var version in local.Where(v => v.Listed))
        {
            if (CranVersion.TryParse(version.Version, out var localVersion))
            {
                var localName = CranManifest.GetName(version.MetadataJson) ?? name;
                files.TryAdd(localVersion.Normalized, (localVersion, CranFileName.Create(localName, localVersion.Text)));
            }
        }

        // Every version of the feed, listed or not, hides the mirror's version of the same normalized form.
        var hidden = local.Select(v => CranVersion.TryParse(v.Version, out var hiddenVersion) ? hiddenVersion.Normalized : null).ToHashSet(StringComparer.Ordinal);
        foreach (var version in upstream.Where(v => !hidden.Contains(v.Version.Normalized)))
        {
            files.TryAdd(version.Version.Normalized, (version.Version, CranFileName.Create(version.Name, version.Version.Text)));
        }

        return files
            .Where(f => f.Key != currentVersion)
            .Select(f => f.Value)
            .OrderBy(f => f.Version)
            .Select(f => f.FileName)
            .ToList();
    }

    public async Task<Stream> GetPackageStreamOrNullAsync(Feed feed, string fileName, CancellationToken cancellationToken)
    {
        if (!CranFileName.TryParse(fileName, out var name, out var version))
        {
            return null;
        }

        var key = CranPackageName.Normalize(name);
        var stored = await FindStoredVersionOrNullAsync(feed, key, version, cancellationToken);
        await _malicious.EnsureAllowedAsync(feed, key, stored?.Version ?? version.Text, stored, cancellationToken);
        stored ??= await MirrorAsync(feed, name, version, cancellationToken);
        if (stored == null)
        {
            return null;
        }

        await _artifacts.AddDownloadAsync(feed.Id, key, stored.Version, cancellationToken);

        return await _storage.GetArchiveStreamAsync(feed.Slug, key, stored.Version, cancellationToken);
    }

    public async Task<CranUploadResult> UploadAsync(Feed feed, string fileName, Stream archive, long maxBytes, CancellationToken cancellationToken)
    {
        string fileNameName = null;
        CranVersion fileNameVersion = null;
        if (fileName != null && !CranFileName.TryParse(fileName, out fileNameName, out fileNameVersion))
        {
            return Invalid($"{fileName} is not the file name of a source package: {{name}}_{{version}}.tar.gz, as R CMD build writes it.");
        }

        ArtifactFileContent content;
        try
        {
            content = await ArtifactFileContent.ReadAsync(archive, maxBytes, cancellationToken);
        }
        catch (ArtifactFileTooLargeException)
        {
            return new CranUploadResult
            {
                Status = CranUploadStatus.TooLarge,
                Message = $"The package is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.",
            };
        }

        string name;
        CranVersion version;
        using (content)
        {
            CranArchive package;
            try
            {
                package = await CranArchiveReader.ReadAsync(content.Stream, cancellationToken);
            }
            catch (InvalidCranPackageException e)
            {
                LogInvalidPackage(e, feed.Slug);
                return Invalid(e.Message);
            }

            name = package.Name;
            version = package.Version;

            if (fileNameName != null
                && (!string.Equals(fileNameName, name, StringComparison.OrdinalIgnoreCase) || fileNameVersion.Normalized != version.Normalized))
            {
                return Invalid($"The file name {fileName} doesn't match the package's DESCRIPTION file, which has {CranFileName.Create(name, version.Text)}.");
            }

            var key = CranPackageName.Normalize(name);
            var existing = await FindStoredVersionOrNullAsync(feed, key, version, cancellationToken);
            if (existing != null)
            {
                // R has no prereleases, so only a feed that allows every overwrite replaces a version.
                if (!_feedSettings.CanOverwrite(feed, isPrerelease: false))
                {
                    return AlreadyExists(name, existing.Version);
                }

                await _artifacts.HardDeleteVersionAsync(feed.Id, key, existing.Version, cancellationToken);
                await _storage.DeleteAsync(feed.Slug, key, existing.Version, cancellationToken);
            }

            var result = await IndexAsync(feed, key, version, package.Description, package.Readme, content, cachedFrom: null, cancellationToken);
            if (result == ArtifactAddResult.AlreadyExists)
            {
                return AlreadyExists(name, version.Text);
            }
        }

        LogUploaded(name, version.Text, feed.Slug);

        return new CranUploadResult { Status = CranUploadStatus.Success, Name = name, Version = version.Text };
    }

    /// <summary>
    /// The current version of a package: the newest listed version of the feed, or the mirror's
    /// current version when it is newer and the feed has no version of its normalized form.
    /// </summary>
    /// <returns>The name and the fields of the version's manifest or index entry; the fields are null when there is none.</returns>
    private static (string Name, IReadOnlyList<KeyValuePair<string, string>> Fields) FindCurrent(
        string name,
        IEnumerable<ArtifactVersion> local,
        CranUpstreamVersion upstream)
    {
        var versions = local.ToList();

        var newest = versions
            .Where(v => v.Listed)
            .Select(v => (Stored: v, Parsed: CranVersion.TryParse(v.Version, out var parsed) ? parsed : null))
            .Where(v => v.Parsed != null)
            .OrderByDescending(v => v.Parsed)
            .FirstOrDefault();

        var hidden = upstream == null
            || versions.Any(v => CranVersion.TryParse(v.Version, out var parsed) && parsed.Normalized == upstream.Version.Normalized);

        if (!hidden && (newest.Stored == null || upstream.Version.CompareTo(newest.Parsed) > 0))
        {
            return (upstream.Name, upstream.IndexEntry);
        }

        if (newest.Stored == null)
        {
            return (null, null);
        }

        var fields = CranManifest.Parse(newest.Stored.MetadataJson);
        return (CranDcf.GetValue(fields, CranManifest.PackageField) ?? name, fields);
    }

    /// <summary>
    /// The stored version of a normalized form, whichever way it is written.
    /// </summary>
    private async Task<ArtifactVersion> FindStoredVersionOrNullAsync(Feed feed, string key, CranVersion version, CancellationToken cancellationToken)
    {
        // Clients ask for the spelling the index gave them, which is the stored one: that is one
        // indexed lookup. Other spellings of the same version are matched against every version.
        var exact = await _artifacts.FindVersionOrNullAsync(feed.Id, key, version.Text, cancellationToken);
        if (exact != null && CranVersion.TryParse(exact.Version, out var exactParsed) && exactParsed.Normalized == version.Normalized)
        {
            return exact;
        }

        var versions = await _artifacts.FindVersionsAsync(feed.Id, key, cancellationToken);

        return versions.FirstOrDefault(v => CranVersion.TryParse(v.Version, out var parsed) && parsed.Normalized == version.Normalized);
    }

    /// <summary>
    /// Stores a version from the feed's mirrors in the feed, unless it is there already.
    /// </summary>
    /// <returns>The stored version, or null if no mirror has it.</returns>
    private async Task<ArtifactVersion> MirrorAsync(Feed feed, string name, CranVersion version, CancellationToken cancellationToken)
    {
        var key = CranPackageName.Normalize(name);
        using var mirrorLock = await _mirrorLock.AcquireAsync(feed.Id, key, version.Normalized, cancellationToken);

        // A concurrent request may have mirrored the version while we waited for the lock.
        var stored = await FindStoredVersionOrNullAsync(feed, key, version, cancellationToken);
        if (stored != null)
        {
            return stored;
        }

        try
        {
            var upstream = (await _upstream.ListVersionsAsync(feed, name, cancellationToken))
                .FirstOrDefault(v => v.Version.Normalized == version.Normalized);
            if (upstream == null)
            {
                return null;
            }

            var maxBytes = _feedSettings.GetMaxPackageSizeBytes(feed);
            using var content = await _upstream.DownloadOrNullAsync(feed, upstream, maxBytes, cancellationToken);
            if (content == null)
            {
                return null;
            }

            // The DESCRIPTION file has more than the index entry. A package this server can't read is
            // still served, with the fields of the mirror's index.
            IReadOnlyList<KeyValuePair<string, string>> description;
            string readme = null;
            try
            {
                var package = await CranArchiveReader.ReadAsync(content.Stream, cancellationToken);
                description = package.Description;
                readme = package.Readme;
            }
            catch (InvalidCranPackageException e)
            {
                LogMirroredPackageUnreadable(e, upstream.Name, upstream.Version.Text);
                description = upstream.IndexEntry
                    ?? [KeyValuePair.Create(CranManifest.PackageField, upstream.Name), KeyValuePair.Create(CranManifest.VersionField, upstream.Version.Text)];
            }

            var result = await IndexAsync(feed, key, upstream.Version, description, readme, content, upstream.Mirror.PackageSource.AbsoluteUri, cancellationToken);

            LogMirrored(upstream.Name, upstream.Version.Text, upstream.Mirror.PackageSource, result);

            // AlreadyExists means another instance stored the version meanwhile, so it is available.
            return await FindStoredVersionOrNullAsync(feed, key, version, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogMirrorFailed(e, name, version.Text);

            return null;
        }
    }

    private async Task<ArtifactAddResult> IndexAsync(
        Feed feed,
        string key,
        CranVersion version,
        IReadOnlyList<KeyValuePair<string, string>> description,
        string readme,
        ArtifactFileContent archive,
        string cachedFrom,
        CancellationToken cancellationToken)
    {
        // The title is the one-line description of a package; the description is a paragraph.
        var summary = (CranDcf.GetValue(description, "Title") ?? CranDcf.GetValue(description, "Description"))?.Trim();

        return await ArtifactPublisher.AddAsync(
            _artifacts,
            _storage,
            feed,
            key,
            new ArtifactVersion
            {
                Version = version.Text,
                IsPrerelease = false,
                Listed = true,
                Description = summary?.Length > MaxDescriptionLength ? summary[..MaxDescriptionLength] : summary,
                MetadataJson = CranManifest.Create(description, archive.Md5),
                ContentHash = archive.Sha256,
                Size = archive.Size,
                CachedFrom = cachedFrom,
                Published = _time.UtcNow,
            },
            archive.Stream,
            readme,
            cancellationToken);
    }

    private static CranUploadResult Invalid(string message)
    {
        return new CranUploadResult { Status = CranUploadStatus.InvalidPackage, Message = message };
    }

    private static CranUploadResult AlreadyExists(string name, string version)
    {
        return new CranUploadResult
        {
            Status = CranUploadStatus.AlreadyExists,
            Name = name,
            Version = version,
            Message = $"Version {version} of package {name} already exists.",
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A source package uploaded to feed {FeedSlug} is invalid")]
    private partial void LogInvalidPackage(Exception exception, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Uploaded package {PackageName} {PackageVersion} to feed {FeedSlug}")]
    private partial void LogUploaded(string packageName, string packageVersion, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mirrored package {PackageName} {PackageVersion} from {PackageSource} with result {Result}")]
    private partial void LogMirrored(string packageName, string packageVersion, Uri packageSource, ArtifactAddResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mirrored package {PackageName} {PackageVersion} could not be read, storing it with the fields of the mirror's index")]
    private partial void LogMirroredPackageUnreadable(Exception exception, string packageName, string packageVersion);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror package {PackageName} {PackageVersion}")]
    private partial void LogMirrorFailed(Exception exception, string packageName, string packageVersion);
}
