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
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Swift;

public partial class SwiftPackageService : ISwiftPackageService
{
    private const long BytesPerMiB = 1024L * 1024;
    private const int MaxDescriptionLength = 4000;

    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly ArtifactIndexCache _indexes;
    private readonly SwiftUpstreamClient _upstream;
    private readonly PackageMirrorLock _mirrorLock;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly SystemTime _time;
    private readonly ILogger<SwiftPackageService> _logger;

    public SwiftPackageService(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        ArtifactIndexCache indexes,
        SwiftUpstreamClient upstream,
        PackageMirrorLock mirrorLock,
        IFeedSettingsResolver feedSettings,
        SystemTime time,
        ILogger<SwiftPackageService> logger)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _indexes = indexes ?? throw new ArgumentNullException(nameof(indexes));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _mirrorLock = mirrorLock ?? throw new ArgumentNullException(nameof(mirrorLock));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SwiftReleaseList> ListReleasesOrNullAsync(Feed feed, string scope, string name, CancellationToken cancellationToken)
    {
        if (!SwiftPackageIdentity.IsValidScope(scope) || !SwiftPackageIdentity.IsValidName(name))
        {
            return null;
        }

        var local = await _artifacts.FindVersionsAsync(feed.Id, SwiftPackageIdentity.Normalize(scope, name), cancellationToken);
        var upstream = await _upstream.ListReleasesAsync(feed, scope, name, cancellationToken);
        if (local.Count == 0 && upstream.Count == 0)
        {
            return null;
        }

        // Every release of the feed, listed or not, hides the mirror's release of the same version.
        var stored = local.Select(v => GetPrecedence(v.Version)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var versions = local
            .Where(v => v.Listed)
            .Select(v => v.Version)
            .Concat(upstream.Select(r => r.Version).Where(v => !stored.Contains(GetPrecedence(v))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderDescending(SwiftVersion.StringComparer)
            .ToList();

        var latest = ArtifactVersionOrder.GetLatest(FeedType.Swift, local);

        return new SwiftReleaseList
        {
            Id = (latest == null ? null : SwiftManifest.Parse(latest.MetadataJson).Id) ?? $"{scope}.{name}",
            Versions = versions,
        };
    }

    public async Task<SwiftRelease> GetReleaseOrNullAsync(Feed feed, string scope, string name, string version, CancellationToken cancellationToken)
    {
        if (!SwiftPackageIdentity.IsValidScope(scope) || !SwiftPackageIdentity.IsValidName(name) || !SwiftVersion.TryParse(version, out _))
        {
            return null;
        }

        var stored = await _artifacts.FindVersionOrNullAsync(feed.Id, SwiftPackageIdentity.Normalize(scope, name), version, cancellationToken)
            ?? await MirrorAsync(feed, scope, name, version, cancellationToken);

        return stored?.ContentHash == null
            ? null
            : new SwiftRelease { Version = stored, Manifest = SwiftManifest.Parse(stored.MetadataJson) };
    }

    public async Task<Stream> GetArchiveStreamAsync(Feed feed, SwiftRelease release, CancellationToken cancellationToken)
    {
        var storedName = release.Version.Artifact.Name;

        await _artifacts.AddDownloadAsync(feed.Id, storedName, release.Version.Version, cancellationToken);

        return await _storage.GetArchiveStreamAsync(feed.Slug, storedName, release.Version.Version, cancellationToken);
    }

    /// <summary>
    /// The identifiers of the feed's packages by each normalized repository URL their releases name,
    /// in the order of the packages' stored names.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> FindIdentifiersByUrlAsync(Feed feed, CancellationToken cancellationToken)
    {
        var byUrl = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var packages = (await _artifacts.FindAllVersionsAsync(feed.Id, cancellationToken))
            .GroupBy(v => v.Artifact.Name)
            .OrderBy(p => p.Key, StringComparer.Ordinal);

        foreach (var package in packages)
        {
            var urls = package
                .SelectMany(v => SwiftReleaseMetadata.GetRepositoryUrls(SwiftManifest.Parse(v.MetadataJson).Metadata))
                .Select(SwiftReleaseMetadata.NormalizeRepositoryUrl)
                .Where(url => url.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

            if (urls.Count == 0)
            {
                continue;
            }

            var latest = ArtifactVersionOrder.GetLatest(FeedType.Swift, package);
            var identifier = SwiftManifest.Parse(latest.MetadataJson).Id ?? package.Key;

            foreach (var url in urls)
            {
                if (!byUrl.TryGetValue(url, out var list))
                {
                    byUrl[url] = list = [];
                }

                list.Add(identifier);
            }
        }

        return byUrl.ToDictionary(e => e.Key, e => (IReadOnlyList<string>)e.Value, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<string>> LookupIdentifiersAsync(Feed feed, string repositoryUrl, CancellationToken cancellationToken)
    {
        var wanted = SwiftReleaseMetadata.NormalizeRepositoryUrl(repositoryUrl);
        var identifiers = new List<string>();

        if (wanted.Length > 0)
        {
            // Swift Package Manager asks for each dependency it has by URL, so the lookup table of
            // the feed is built once, and again after a package of the feed changed.
            var byUrl = await _indexes.GetOrBuildAsync(
                feed,
                "swift/identifiers",
                ArtifactIndexCache.LocalIndexDuration,
                () => FindIdentifiersByUrlAsync(feed, cancellationToken),
                table => table.Sum(e => e.Key.Length + e.Value.Sum(id => id.Length)) * sizeof(char));

            identifiers.AddRange(byUrl.GetValueOrDefault(wanted) ?? []);
        }

        foreach (var identifier in await _upstream.LookupIdentifiersAsync(feed, repositoryUrl, cancellationToken))
        {
            if (!identifiers.Contains(identifier, StringComparer.OrdinalIgnoreCase))
            {
                identifiers.Add(identifier);
            }
        }

        return identifiers;
    }

    public async Task<SwiftPublishResult> PublishAsync(
        Feed feed,
        string scope,
        string name,
        string version,
        Stream archive,
        string metadata,
        string signature,
        string signatureFormat,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (!SwiftPackageIdentity.IsValidScope(scope))
        {
            return Result(SwiftPublishStatus.InvalidRequest,
                $"{scope} is not a valid scope: letters, digits and single hyphens between them, at most {SwiftPackageIdentity.MaxScopeLength} characters.");
        }

        if (!SwiftPackageIdentity.IsValidName(name))
        {
            return Result(SwiftPublishStatus.InvalidRequest,
                $"{name} is not a valid package name: letters, digits and single hyphens or underscores between them, " +
                $"at most {SwiftPackageIdentity.MaxNameLength} characters.");
        }

        if (!SwiftVersion.TryParse(version, out var parsed))
        {
            return Result(SwiftPublishStatus.InvalidRequest, $"{version} is not a semantic version, such as 1.0.0 or 2.1.0-beta.1.");
        }

        if (signature != null && signatureFormat != SwiftManifest.CmsSignatureFormat)
        {
            return Result(SwiftPublishStatus.InvalidPackage,
                $"The signature format {signatureFormat ?? "(none)"} is not supported. Swift Package Manager signs with {SwiftManifest.CmsSignatureFormat}.");
        }

        if (signature != null && !IsBase64(signature))
        {
            return Result(SwiftPublishStatus.InvalidPackage, "The signature of the source archive is not valid base64.");
        }

        ArtifactFileContent content;
        try
        {
            content = await ArtifactFileContent.ReadAsync(archive, maxBytes, cancellationToken);
        }
        catch (ArtifactFileTooLargeException)
        {
            return Result(SwiftPublishStatus.TooLarge, $"The source archive is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.");
        }

        var id = $"{scope}.{name}";
        var storedName = SwiftPackageIdentity.Normalize(scope, name);

        using (content)
        {
            SwiftArchive package;
            JsonObject releaseMetadata;
            try
            {
                package = SwiftArchiveReader.Read(content.Stream);
                releaseMetadata = SwiftReleaseMetadata.Read(metadata);
            }
            catch (InvalidSwiftPackageException e)
            {
                LogInvalidPackage(e, feed.Slug);
                return Result(SwiftPublishStatus.InvalidPackage, e.Message);
            }

            // Versions that differ only in their build metadata are the same version.
            var existing = (await _artifacts.FindVersionsAsync(feed.Id, storedName, cancellationToken))
                .FirstOrDefault(v => string.Equals(GetPrecedence(v.Version), parsed.Precedence, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                if (!_feedSettings.CanOverwrite(feed, parsed.IsPrerelease))
                {
                    return AlreadyExists(id, existing.Version);
                }

                await _artifacts.HardDeleteVersionAsync(feed.Id, storedName, existing.Version, cancellationToken);
                await _storage.DeleteAsync(feed.Slug, storedName, existing.Version, cancellationToken);
            }

            var manifest = SwiftManifest.Create(id, version, releaseMetadata, signature, signatureFormat, _time.UtcNow, package.Manifests);
            var result = await IndexAsync(feed, storedName, parsed, manifest, package.Readme, content, cachedFrom: null, cancellationToken);
            if (result == ArtifactAddResult.AlreadyExists)
            {
                return AlreadyExists(id, version);
            }
        }

        LogPublished(id, version, feed.Slug);

        return Result(SwiftPublishStatus.Success, null);
    }

    /// <summary>
    /// Stores a release from the feed's mirrors in the feed, unless it is there already.
    /// </summary>
    /// <returns>The stored version, or null if no mirror has it.</returns>
    private async Task<ArtifactVersion> MirrorAsync(Feed feed, string scope, string name, string version, CancellationToken cancellationToken)
    {
        var release = (await _upstream.ListReleasesAsync(feed, scope, name, cancellationToken))
            .FirstOrDefault(r => r.Version == version);
        if (release == null || !SwiftVersion.TryParse(release.Version, out var parsed))
        {
            return null;
        }

        var storedName = SwiftPackageIdentity.Normalize(scope, name);
        using var mirrorLock = await _mirrorLock.AcquireAsync(feed.Id, storedName, release.Version, cancellationToken);

        // A concurrent request may have mirrored the release while we waited for the lock.
        var stored = await _artifacts.FindVersionOrNullAsync(feed.Id, storedName, release.Version, cancellationToken);
        if (stored != null)
        {
            return stored;
        }

        try
        {
            var metadata = await _upstream.GetReleaseOrNullAsync(feed, release, scope, name, cancellationToken);
            var resource = (metadata?["resources"] as JsonArray)?
                .OfType<JsonObject>()
                .FirstOrDefault(r => GetString(r, "name") == SwiftManifest.SourceArchiveResourceName);
            if (resource == null)
            {
                LogMirroredReleaseInvalid(storedName, release.Version, release.Mirror.PackageSource);
                return null;
            }

            var maxBytes = _feedSettings.GetMaxPackageSizeBytes(feed);
            using var archive = await _upstream.DownloadArchiveOrNullAsync(feed, release, scope, name, maxBytes, cancellationToken);
            if (archive == null)
            {
                return null;
            }

            // Clients check the archive against the checksum, so a mirror's archive must match its own.
            var checksum = GetString(resource, "checksum");
            if (checksum != null && !string.Equals(checksum, archive.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                LogChecksumMismatch(storedName, release.Version, release.Mirror.PackageSource);
                return null;
            }

            SwiftArchive package;
            try
            {
                package = SwiftArchiveReader.Read(archive.Stream);
            }
            catch (InvalidSwiftPackageException e)
            {
                LogMirroredArchiveInvalid(e, storedName, release.Version);
                return null;
            }

            JsonObject releaseMetadata;
            try
            {
                releaseMetadata = SwiftReleaseMetadata.Read(metadata["metadata"]?.ToJsonString());
            }
            catch (InvalidSwiftPackageException)
            {
                releaseMetadata = new JsonObject();
            }

            var signing = resource["signing"] as JsonObject;
            var id = GetString(metadata, "id") is { } mirrorId
                && SwiftPackageIdentity.TryParse(mirrorId, out var mirrorScope, out var mirrorName)
                && SwiftPackageIdentity.Normalize(mirrorScope, mirrorName) == storedName
                    ? mirrorId
                    : $"{scope}.{name}";

            var manifest = SwiftManifest.Create(
                id,
                release.Version,
                releaseMetadata,
                GetString(signing, "signatureBase64Encoded"),
                GetString(signing, "signatureFormat"),
                GetString(metadata, "publishedAt") ?? SwiftManifest.FormatTime(_time.UtcNow),
                package.Manifests);

            var result = await IndexAsync(
                feed, storedName, parsed, manifest, package.Readme, archive, release.Mirror.PackageSource.AbsoluteUri, cancellationToken);

            LogMirrored(storedName, release.Version, release.Mirror.PackageSource, result);

            // AlreadyExists means another instance stored the release meanwhile, so it is available.
            return await _artifacts.FindVersionOrNullAsync(feed.Id, storedName, release.Version, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogMirrorFailed(e, storedName, release.Version);

            return null;
        }
    }

    private async Task<ArtifactAddResult> IndexAsync(
        Feed feed,
        string storedName,
        SwiftVersion version,
        SwiftManifest manifest,
        string readme,
        ArtifactFileContent archive,
        string cachedFrom,
        CancellationToken cancellationToken)
    {
        var description = GetString(manifest.Metadata, "description")?.Trim();

        return await ArtifactPublisher.AddAsync(
            _artifacts,
            _storage,
            feed,
            storedName,
            new ArtifactVersion
            {
                Version = version.ToString(),
                IsPrerelease = version.IsPrerelease,
                Listed = true,
                Description = description?.Length > MaxDescriptionLength ? description[..MaxDescriptionLength] : description,
                MetadataJson = manifest.ToJson(),
                ContentHash = archive.Sha256,
                Size = archive.Size,
                CachedFrom = cachedFrom,
                Published = _time.UtcNow,
            },
            archive.Stream,
            readme,
            cancellationToken);
    }

    private static string GetPrecedence(string version)
    {
        return SwiftVersion.TryParse(version, out var parsed) ? parsed.Precedence : version;
    }

    private static bool IsBase64(string text)
    {
        var buffer = new byte[text.Length];
        return Convert.TryFromBase64String(text, buffer, out var written) && written > 0;
    }

    private static string GetString(JsonObject item, string property)
    {
        return item?[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    private static SwiftPublishResult Result(SwiftPublishStatus status, string message)
    {
        return new SwiftPublishResult { Status = status, Message = message };
    }

    private static SwiftPublishResult AlreadyExists(string id, string version)
    {
        return Result(SwiftPublishStatus.AlreadyExists, $"Version {version} of package {id} already exists.");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A Swift package published to feed {FeedSlug} is invalid")]
    private partial void LogInvalidPackage(Exception exception, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published Swift package {PackageName} {PackageVersion} to feed {FeedSlug}")]
    private partial void LogPublished(string packageName, string packageVersion, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mirrored Swift package {PackageName} {PackageVersion} from {PackageSource} with result {Result}")]
    private partial void LogMirrored(string packageName, string packageVersion, Uri packageSource, ArtifactAddResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mirrored release {PackageName} {PackageVersion} from {PackageSource} has no source archive")]
    private partial void LogMirroredReleaseInvalid(string packageName, string packageVersion, Uri packageSource);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The source archive of {PackageName} {PackageVersion} from {PackageSource} does not match the mirror's checksum")]
    private partial void LogChecksumMismatch(string packageName, string packageVersion, Uri packageSource);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mirrored source archive of {PackageName} {PackageVersion} is invalid")]
    private partial void LogMirroredArchiveInvalid(Exception exception, string packageName, string packageVersion);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror Swift package {PackageName} {PackageVersion}")]
    private partial void LogMirrorFailed(Exception exception, string packageName, string packageVersion);
}
