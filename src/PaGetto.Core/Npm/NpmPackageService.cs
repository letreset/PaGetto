using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Npm;

public partial class NpmPackageService : INpmPackageService
{
    /// <summary>
    /// The tag every package has. Clients install the version it points at by default.
    /// </summary>
    public const string LatestTag = ArtifactVersionOrder.LatestTag;

    /// <summary>
    /// The message of a version that was unlisted in the web UI, which has no message of its own.
    /// </summary>
    public const string DefaultDeprecationMessage = "This version is deprecated.";

    // The lengths of the description and tag name columns.
    private const int MaxDescriptionLength = 4000;
    private const int MaxTagLength = 128;

    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly NpmUpstreamClient _upstream;
    private readonly PackageMirrorLock _mirrorLock;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly SystemTime _time;
    private readonly ILogger<NpmPackageService> _logger;

    public NpmPackageService(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        NpmUpstreamClient upstream,
        PackageMirrorLock mirrorLock,
        IFeedSettingsResolver feedSettings,
        SystemTime time,
        ILogger<NpmPackageService> logger)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _mirrorLock = mirrorLock ?? throw new ArgumentNullException(nameof(mirrorLock));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<JsonObject> GetPackumentOrNullAsync(
        Feed feed,
        string name,
        bool localOnly,
        Func<string, string, string> getTarballUrl,
        CancellationToken cancellationToken)
    {
        var package = await FindPackageOrNullAsync(feed, name, localOnly, cancellationToken);
        if (package == null)
        {
            return null;
        }

        var versions = new JsonObject();
        var times = new JsonObject();
        foreach (var version in package.Versions)
        {
            versions[version.Version] = ToPackumentVersion(version, name, getTarballUrl(name, version.Version));

            if (version.Published.HasValue)
            {
                times[version.Version] = FormatTime(version.Published.Value);
            }
        }

        var published = package.Versions.Where(v => v.Published.HasValue).Select(v => v.Published.Value).ToList();
        if (published.Count > 0)
        {
            times["created"] = FormatTime(published.Min());
            times["modified"] = FormatTime(published.Max());
        }

        var distTags = new JsonObject();
        foreach (var (tag, version) in package.DistTags)
        {
            distTags[tag] = version;
        }

        var latest = package.Versions.First(v => v.Version == package.DistTags[LatestTag]);

        return new JsonObject
        {
            ["_id"] = name,

            // Clients send it back when they change the package; any value is accepted.
            ["_rev"] = $"{package.Versions.Count}-{(published.Count > 0 ? published.Max().Ticks : 0):x}",
            ["name"] = name,
            ["description"] = GetString(latest.Manifest, "description"),
            ["dist-tags"] = distTags,
            ["versions"] = versions,
            ["time"] = times,
        };
    }

    public async Task<IReadOnlyDictionary<string, string>> GetDistTagsOrNullAsync(Feed feed, string name, CancellationToken cancellationToken)
    {
        return (await FindPackageOrNullAsync(feed, name, localOnly: false, cancellationToken))?.DistTags;
    }

    public async Task<Stream> GetTarballStreamOrNullAsync(Feed feed, string name, string version, CancellationToken cancellationToken)
    {
        if (!NpmPackageName.IsValid(name) || !NpmVersion.TryParse(version, out _))
        {
            return null;
        }

        var storedName = NpmPackageName.Normalize(name);
        if (await _artifacts.FindVersionOrNullAsync(feed.Id, storedName, version, cancellationToken) == null
            && !await MirrorAsync(feed, name, version, cancellationToken))
        {
            return null;
        }

        await _artifacts.AddDownloadAsync(feed.Id, storedName, version, cancellationToken);

        return await _storage.GetArchiveStreamAsync(feed.Slug, storedName, version, cancellationToken);
    }

    public async Task<NpmResult> PublishAsync(Feed feed, string name, JsonElement body, long maxTarballBytes, CancellationToken cancellationToken)
    {
        if (!NpmPackageName.IsValidForPublish(name))
        {
            return Invalid(
                $"'{name}' is not a valid package name: use lowercase letters, digits, '-', '.', '_' and '~', " +
                $"an optional @scope/ prefix, and at most {NpmPackageName.MaxLength} characters.");
        }

        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("versions", out var versions)
            || versions.ValueKind != JsonValueKind.Object
            || versions.EnumerateObject().Count() != 1)
        {
            return Invalid("The request must contain exactly one version.");
        }

        if (!body.TryGetProperty("_attachments", out var attachments)
            || attachments.ValueKind != JsonValueKind.Object
            || attachments.EnumerateObject().Count() != 1)
        {
            return Invalid("The request must contain exactly one tarball.");
        }

        var versionProperty = versions.EnumerateObject().Single();
        var version = versionProperty.Name;
        if (!NpmVersion.TryParse(version, out var parsedVersion))
        {
            return Invalid($"'{version}' is not a semantic version, e.g. 1.2.3.");
        }

        if (JsonNode.Parse(versionProperty.Value.GetRawText()) is not JsonObject manifest
            || GetString(manifest, "name") != name
            || GetString(manifest, "version") != version)
        {
            return Invalid($"The manifest of version {version} must have the name {name} and the version {version}.");
        }

        var attachment = attachments.EnumerateObject().Single().Value;
        if (attachment.ValueKind != JsonValueKind.Object
            || !attachment.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.String)
        {
            return Invalid("The tarball must be sent as base64 data.");
        }

        // Base64 takes 4 characters for 3 bytes, so the size is known before decoding.
        if (data.GetString().Length / 4L * 3 > maxTarballBytes + 2)
        {
            return Failure(NpmStatus.TooLarge, $"The package is larger than the feed's limit of {maxTarballBytes / 1024 / 1024} MiB.", version);
        }

        if (!data.TryGetBytesFromBase64(out var bytes) || bytes.Length == 0)
        {
            return Invalid("The tarball is not valid base64 data.");
        }

        if (bytes.Length > maxTarballBytes)
        {
            return Failure(NpmStatus.TooLarge, $"The package is larger than the feed's limit of {maxTarballBytes / 1024 / 1024} MiB.", version);
        }

        using var tarball = new MemoryStream(bytes, writable: false);

        NpmArchive content;
        try
        {
            content = await NpmArchiveReader.ReadAsync(tarball, cancellationToken);
        }
        catch (InvalidNpmPackageException e)
        {
            LogInvalidPackage(e, feed.Slug);

            return Invalid(e.Message, version);
        }

        if (content.Name != name || content.Version.ToString() != version)
        {
            return Invalid($"The tarball's package.json has {content.Name} {content.Version}, but {name} {version} is being published.", version);
        }

        tarball.Position = 0;
        var hashes = await NpmTarballHashes.ComputeAsync(tarball, cancellationToken);

        var dist = manifest["dist"] as JsonObject;
        if (!hashes.Matches(GetString(dist, "integrity"), GetString(dist, "shasum")))
        {
            return Invalid("The tarball does not match the integrity of its manifest.", version);
        }

        if (await _artifacts.FindVersionOrNullAsync(feed.Id, name, version, cancellationToken) != null)
        {
            var allowOverwrites = _feedSettings.GetAllowPackageOverwrites(feed);
            if (allowOverwrites == PackageOverwriteAllowed.False
                || (allowOverwrites == PackageOverwriteAllowed.PrereleaseOnly && !parsedVersion.IsPrerelease))
            {
                return AlreadyExists(name, version);
            }

            await _artifacts.HardDeleteVersionAsync(feed.Id, name, version, cancellationToken);
            await _storage.DeleteAsync(feed.Slug, name, version, cancellationToken);
        }

        // A new version is not deprecated, whatever the manifest says.
        manifest.Remove("deprecated");

        var result = await IndexAsync(
            feed,
            name,
            parsedVersion,
            manifest,
            content.Readme,
            tarball,
            hashes,
            cachedFrom: null,
            published: _time.UtcNow,
            listed: true,
            cancellationToken);

        if (result == ArtifactAddResult.AlreadyExists)
        {
            return AlreadyExists(name, version);
        }

        var tagged = false;
        if (body.TryGetProperty("dist-tags", out var distTags) && distTags.ValueKind == JsonValueKind.Object)
        {
            foreach (var tag in distTags.EnumerateObject())
            {
                if (tag.Value.ValueKind == JsonValueKind.String && tag.Value.GetString() == version && IsValidTag(tag.Name))
                {
                    await _artifacts.SetTagAsync(feed.Id, name, tag.Name, version, cancellationToken);
                    tagged = true;
                }
            }
        }

        if (!tagged)
        {
            await _artifacts.SetTagAsync(feed.Id, name, LatestTag, version, cancellationToken);
        }

        LogPublished(name, version, feed.Slug);

        return NpmResult.Success(version);
    }

    public async Task<NpmResult> UpdateAsync(
        Feed feed,
        string name,
        JsonElement body,
        bool canDeprecate,
        bool canUnpublish,
        CancellationToken cancellationToken)
    {
        if (!NpmPackageName.IsValid(name))
        {
            return NotFound(name);
        }

        var storedName = NpmPackageName.Normalize(name);
        var local = await _artifacts.FindVersionsAsync(feed.Id, storedName, cancellationToken);
        if (local.Count == 0)
        {
            return NotFound(name);
        }

        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("versions", out var versions)
            || versions.ValueKind != JsonValueKind.Object)
        {
            return Invalid("The request must contain the package's versions.");
        }

        var requested = versions.EnumerateObject().ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase);

        var removed = local.Where(v => !requested.ContainsKey(v.Version)).ToList();
        var changed = new List<(ArtifactVersion Version, string Deprecated)>();
        foreach (var version in local.Where(v => requested.ContainsKey(v.Version)))
        {
            var message = requested[version.Version].ValueKind == JsonValueKind.Object
                && requested[version.Version].TryGetProperty("deprecated", out var deprecated)
                && deprecated.ValueKind == JsonValueKind.String
                && deprecated.GetString().Length > 0
                ? deprecated.GetString()
                : null;

            if (message != GetDeprecationMessage(version))
            {
                changed.Add((version, message));
            }
        }

        if ((removed.Count > 0 && !canUnpublish) || (changed.Count > 0 && !canDeprecate))
        {
            return Failure(
                NpmStatus.Forbidden,
                removed.Count > 0 && !canUnpublish
                    ? "You do not have permission to unpublish packages from this feed."
                    : "You do not have permission to deprecate packages of this feed.");
        }

        foreach (var version in removed)
        {
            await DeleteVersionAsync(feed, storedName, version.Version, cancellationToken);
        }

        foreach (var (version, message) in changed)
        {
            var manifest = ParseObject(version.MetadataJson) ?? new JsonObject();
            if (message == null)
            {
                manifest.Remove("deprecated");
            }
            else
            {
                manifest["deprecated"] = message;
            }

            await _artifacts.UpdateVersionAsync(
                feed.Id, storedName, version.Version, listed: message == null, manifest.ToJsonString(), cancellationToken);

            LogDeprecationChanged(name, version.Version, feed.Slug, message != null);
        }

        return new NpmResult { Status = NpmStatus.Success, Unpublished = removed.Select(v => v.Version).ToList() };
    }

    public async Task<NpmResult> UnpublishAsync(Feed feed, string name, string version, CancellationToken cancellationToken)
    {
        if (!NpmPackageName.IsValid(name))
        {
            return NotFound(name);
        }

        var storedName = NpmPackageName.Normalize(name);
        var local = await _artifacts.FindVersionsAsync(feed.Id, storedName, cancellationToken);
        var removed = version == null
            ? local
            : local.Where(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase)).ToList();

        if (removed.Count == 0)
        {
            return NotFound(name, version);
        }

        foreach (var found in removed)
        {
            await DeleteVersionAsync(feed, storedName, found.Version, cancellationToken);
        }

        return new NpmResult { Status = NpmStatus.Success, Version = version, Unpublished = removed.Select(v => v.Version).ToList() };
    }

    public async Task<NpmResult> SetDistTagAsync(Feed feed, string name, string tag, string version, CancellationToken cancellationToken)
    {
        if (!IsValidTag(tag))
        {
            return Invalid($"'{tag}' is not a valid tag: it must start with a letter, and must not be a version.");
        }

        if (!NpmPackageName.IsValid(name)
            || string.IsNullOrEmpty(version)
            || !await _artifacts.SetTagAsync(feed.Id, NpmPackageName.Normalize(name), tag, version, cancellationToken))
        {
            return NotFound(name, version);
        }

        LogTagSet(name, tag, version, feed.Slug);

        return NpmResult.Success(version);
    }

    public async Task<NpmResult> RemoveDistTagAsync(Feed feed, string name, string tag, CancellationToken cancellationToken)
    {
        if (tag == LatestTag)
        {
            return Invalid("The latest tag can't be removed.");
        }

        if (!NpmPackageName.IsValid(name)
            || !await _artifacts.RemoveTagAsync(feed.Id, NpmPackageName.Normalize(name), tag, cancellationToken))
        {
            return Failure(NpmStatus.NotFound, $"Package {name} has no tag {tag} in this feed.");
        }

        LogTagRemoved(name, tag, feed.Slug);

        return NpmResult.Success();
    }

    private async Task<NpmPackage> FindPackageOrNullAsync(Feed feed, string name, bool localOnly, CancellationToken cancellationToken)
    {
        if (!NpmPackageName.IsValid(name))
        {
            return null;
        }

        var versions = new Dictionary<string, NpmPackageVersion>(StringComparer.OrdinalIgnoreCase);
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!localOnly)
        {
            var upstream = await _upstream.ListVersionsAsync(feed, name, cancellationToken);
            foreach (var version in upstream)
            {
                versions[version.Version] = new NpmPackageVersion
                {
                    Version = version.Version,
                    Manifest = ParseObject(version.ManifestJson),
                    Listed = version.Deprecated == null,
                    Published = version.Published,
                };
            }

            foreach (var (tag, version) in upstream.FirstOrDefault()?.DistTags ?? new Dictionary<string, string>())
            {
                tags[tag] = version;
            }
        }

        var storedName = NpmPackageName.Normalize(name);
        foreach (var version in await _artifacts.FindVersionsAsync(feed.Id, storedName, cancellationToken))
        {
            versions[version.Version] = new NpmPackageVersion
            {
                Version = version.Version,
                Manifest = ParseObject(version.MetadataJson),
                Listed = version.Listed,
                Published = version.Published,
            };
        }

        if (versions.Count == 0)
        {
            return null;
        }

        foreach (var tag in await _artifacts.FindTagsAsync(feed.Id, storedName, cancellationToken))
        {
            tags[tag.Name] = tag.Version;
        }

        var sorted = versions.Values.OrderBy(v => v.Version, NpmVersion.StringComparer).ToList();

        // Tags of versions that are gone are dropped, and use the version's spelling.
        var distTags = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (tag, version) in tags)
        {
            if (versions.TryGetValue(version, out var target))
            {
                distTags[tag] = target.Version;
            }
        }

        if (!distTags.ContainsKey(LatestTag))
        {
            // Like the npm registry: the newest listed stable version, then the newest listed one.
            distTags[LatestTag] = (sorted.LastOrDefault(v => v.Listed && !IsPrerelease(v.Version))
                ?? sorted.LastOrDefault(v => v.Listed)
                ?? sorted.Last()).Version;
        }

        return new NpmPackage { Versions = sorted, DistTags = distTags };
    }

    /// <summary>
    /// Stores a version from the feed's mirrors in the feed, unless it is there already.
    /// </summary>
    /// <returns>True if the feed has the version afterwards.</returns>
    private async Task<bool> MirrorAsync(Feed feed, string name, string version, CancellationToken cancellationToken)
    {
        var storedName = NpmPackageName.Normalize(name);
        using var mirrorLock = await _mirrorLock.AcquireAsync(feed.Id, storedName, version, cancellationToken);

        // A concurrent request may have mirrored the version while we waited for the lock.
        if (await _artifacts.FindVersionOrNullAsync(feed.Id, storedName, version, cancellationToken) != null)
        {
            return true;
        }

        var upstream = (await _upstream.ListVersionsAsync(feed, name, cancellationToken))
            .FirstOrDefault(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));

        if (upstream == null || !NpmVersion.TryParse(upstream.Version, out var parsedVersion))
        {
            return false;
        }

        try
        {
            using var tarball = await _upstream.DownloadTarballOrNullAsync(feed, upstream, cancellationToken);
            if (tarball == null)
            {
                return false;
            }

            tarball.Position = 0;
            var hashes = await NpmTarballHashes.ComputeAsync(tarball, cancellationToken);
            if (!hashes.Matches(upstream.Integrity, upstream.Shasum))
            {
                throw new InvalidDataException(
                    $"The tarball of {name} {version} does not match the integrity the upstream lists.");
            }

            // The readme is only shown in the web UI, so a mirrored tarball without a readable one is still served.
            string readme = null;
            try
            {
                tarball.Position = 0;
                readme = (await NpmArchiveReader.ReadAsync(tarball, cancellationToken)).Readme;
            }
            catch (InvalidNpmPackageException e)
            {
                LogMirroredTarballUnreadable(e, name, version);
            }

            var result = await IndexAsync(
                feed,
                storedName,
                parsedVersion,
                ParseObject(upstream.ManifestJson) ?? new JsonObject(),
                readme,
                tarball,
                hashes,
                cachedFrom: upstream.Mirror.PackageSource.AbsoluteUri,
                published: upstream.Published ?? _time.UtcNow,
                listed: upstream.Deprecated == null,
                cancellationToken);

            LogMirrored(name, version, upstream.Mirror.PackageSource, result);

            // AlreadyExists means another instance stored the version meanwhile, so it is available.
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogMirrorFailed(e, name, version);

            return false;
        }
    }

    private async Task<ArtifactAddResult> IndexAsync(
        Feed feed,
        string storedName,
        NpmVersion version,
        JsonObject manifest,
        string readme,
        Stream tarball,
        NpmTarballHashes hashes,
        string cachedFrom,
        DateTime published,
        bool listed,
        CancellationToken cancellationToken)
    {
        // The readme is stored next to the tarball, and the tarball's URL depends on the request.
        manifest.Remove("readme");
        manifest["dist"] = CreateDist(manifest["dist"] as JsonObject, hashes.Integrity, hashes.Shasum, tarballUrl: null);

        tarball.Position = 0;
        using var readmeStream = readme == null ? null : new MemoryStream(Encoding.UTF8.GetBytes(readme));

        await _storage.SaveAsync(feed.Slug, storedName, version.ToString(), tarball, readmeStream, cancellationToken);

        return await _artifacts.AddVersionAsync(
            feed.Id,
            storedName,
            new ArtifactVersion
            {
                Version = version.ToString(),
                IsPrerelease = version.IsPrerelease,
                Listed = listed,
                Description = Truncate(GetString(manifest, "description")?.Trim(), MaxDescriptionLength),
                MetadataJson = manifest.ToJsonString(),
                ContentHash = hashes.Sha256,
                Size = tarball.Length,
                HasReadme = readme != null,
                CachedFrom = cachedFrom,
                Published = published,
            },
            cancellationToken);
    }

    private async Task DeleteVersionAsync(Feed feed, string storedName, string version, CancellationToken cancellationToken)
    {
        if (await _artifacts.HardDeleteVersionAsync(feed.Id, storedName, version, cancellationToken))
        {
            await _storage.DeleteAsync(feed.Slug, storedName, version, cancellationToken);
            LogUnpublished(storedName, version, feed.Slug);
        }
    }

    /// <summary>
    /// A version's manifest as the package document lists it: with this feed's tarball URL, and
    /// with a deprecation message exactly when the version is unlisted.
    /// </summary>
    private static JsonObject ToPackumentVersion(NpmPackageVersion version, string name, string tarballUrl)
    {
        var manifest = version.Manifest?.DeepClone() as JsonObject ?? new JsonObject();

        manifest["name"] ??= name;
        manifest["version"] = version.Version;
        manifest["_id"] = $"{name}@{version.Version}";

        var dist = manifest["dist"] as JsonObject;
        manifest["dist"] = CreateDist(dist, GetString(dist, "integrity"), GetString(dist, "shasum"), tarballUrl);

        if (version.Listed)
        {
            manifest.Remove("deprecated");
        }
        else if (string.IsNullOrEmpty(GetString(manifest, "deprecated")))
        {
            manifest["deprecated"] = DefaultDeprecationMessage;
        }

        return manifest;
    }

    /// <summary>
    /// The <c>dist</c> of a manifest. Only what describes the tarball itself is kept: signatures
    /// and attestations of another registry don't apply to this one.
    /// </summary>
    private static JsonObject CreateDist(JsonObject existing, string integrity, string shasum, string tarballUrl)
    {
        var dist = new JsonObject();

        if (tarballUrl != null) dist["tarball"] = tarballUrl;
        if (!string.IsNullOrEmpty(integrity)) dist["integrity"] = integrity;
        if (!string.IsNullOrEmpty(shasum)) dist["shasum"] = shasum;

        foreach (var property in new[] { "fileCount", "unpackedSize" })
        {
            if (existing?[property] is JsonValue value)
            {
                dist[property] = value.DeepClone();
            }
        }

        return dist;
    }

    /// <summary>
    /// The deprecation message clients see for a version, or null if it is not deprecated.
    /// </summary>
    private static string GetDeprecationMessage(ArtifactVersion version)
    {
        if (version.Listed)
        {
            return null;
        }

        var message = GetString(ParseObject(version.MetadataJson), "deprecated");
        return string.IsNullOrEmpty(message) ? DefaultDeprecationMessage : message;
    }

    /// <summary>
    /// A dist-tag starts with a letter, so that it can't be mistaken for a version or a range.
    /// </summary>
    private static bool IsValidTag(string tag)
    {
        return !string.IsNullOrEmpty(tag)
            && tag.Length <= MaxTagLength
            && TagRegex().IsMatch(tag);
    }

    private static bool IsPrerelease(string version)
    {
        return NpmVersion.TryParse(version, out var parsed) && parsed.IsPrerelease;
    }

    private static JsonObject ParseObject(string json)
    {
        try
        {
            return string.IsNullOrEmpty(json) ? null : JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetString(JsonObject json, string property)
    {
        return json?[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    private static string FormatTime(DateTime utc)
    {
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    private static string Truncate(string text, int maxLength)
    {
        return text?.Length > maxLength ? text[..maxLength] : text;
    }

    private static NpmResult Invalid(string message, string version = null)
    {
        return Failure(NpmStatus.Invalid, message, version);
    }

    private static NpmResult NotFound(string name, string version = null)
    {
        return Failure(
            NpmStatus.NotFound,
            version == null ? $"Package {name} was not found in this feed." : $"Version {version} of package {name} was not found in this feed.",
            version);
    }

    private static NpmResult AlreadyExists(string name, string version)
    {
        return Failure(NpmStatus.AlreadyExists, $"Version {version} of package {name} already exists.", version);
    }

    private static NpmResult Failure(NpmStatus status, string message, string version = null)
    {
        return NpmResult.Failure(status, message, version);
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [LoggerMessage(Level = LogLevel.Warning, Message = "An npm package published to feed {FeedSlug} is invalid")]
    private partial void LogInvalidPackage(Exception exception, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published npm package {PackageName} {PackageVersion} to feed {FeedSlug}")]
    private partial void LogPublished(string packageName, string packageVersion, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Unpublished npm package {PackageName} {PackageVersion} from feed {FeedSlug}")]
    private partial void LogUnpublished(string packageName, string packageVersion, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Changed the deprecation of npm package {PackageName} {PackageVersion} in feed {FeedSlug}: deprecated {Deprecated}")]
    private partial void LogDeprecationChanged(string packageName, string packageVersion, string feedSlug, bool deprecated);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tagged npm package {PackageName} {PackageVersion} as {Tag} in feed {FeedSlug}")]
    private partial void LogTagSet(string packageName, string tag, string packageVersion, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed tag {Tag} of npm package {PackageName} in feed {FeedSlug}")]
    private partial void LogTagRemoved(string packageName, string tag, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mirrored npm package {PackageName} {PackageVersion} from {PackageSource} with result {Result}")]
    private partial void LogMirrored(string packageName, string packageVersion, Uri packageSource, ArtifactAddResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mirrored tarball of npm package {PackageName} {PackageVersion} could not be read, storing it without a readme")]
    private partial void LogMirroredTarballUnreadable(Exception exception, string packageName, string packageVersion);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror npm package {PackageName} {PackageVersion}")]
    private partial void LogMirrorFailed(Exception exception, string packageName, string packageVersion);
}
