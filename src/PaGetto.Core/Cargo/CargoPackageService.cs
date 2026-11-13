using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Cargo;

public partial class CargoPackageService : ICargoPackageService
{
    // The length of the description column.
    private const int MaxDescriptionLength = 4000;

    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly CargoUpstreamClient _upstream;
    private readonly PackageMirrorLock _mirrorLock;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly SystemTime _time;
    private readonly ILogger<CargoPackageService> _logger;

    public CargoPackageService(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        CargoUpstreamClient upstream,
        PackageMirrorLock mirrorLock,
        IFeedSettingsResolver feedSettings,
        SystemTime time,
        ILogger<CargoPackageService> logger)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _mirrorLock = mirrorLock ?? throw new ArgumentNullException(nameof(mirrorLock));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<CargoIndexVersion>> FindVersionsAsync(Feed feed, string name, CancellationToken cancellationToken)
    {
        if (!CargoPackageName.IsValid(name))
        {
            return [];
        }

        name = name.ToLowerInvariant();
        var versions = new Dictionary<string, CargoIndexVersion>(StringComparer.OrdinalIgnoreCase);

        foreach (var upstream in await _upstream.ListVersionsAsync(feed, name, cancellationToken))
        {
            versions[upstream.Version] = new CargoIndexVersion
            {
                Version = upstream.Version,
                ManifestJson = upstream.IndexLineJson,
                Cksum = upstream.Cksum,
                Yanked = upstream.Yanked,
            };
        }

        foreach (var local in await _artifacts.FindVersionsAsync(feed.Id, name, cancellationToken))
        {
            versions[local.Version] = new CargoIndexVersion
            {
                Version = local.Version,
                ManifestJson = local.MetadataJson,
                Cksum = local.ContentHash,
                Yanked = !local.Listed,
            };
        }

        return versions.Values.OrderBy(v => v.Version, CargoVersion.StringComparer).ToList();
    }

    public async Task<Stream> GetCrateStreamOrNullAsync(Feed feed, string name, string version, CancellationToken cancellationToken)
    {
        if (!CargoPackageName.IsValid(name) || !CargoVersion.TryParse(version, out _))
        {
            return null;
        }

        name = name.ToLowerInvariant();

        if (await _artifacts.FindVersionOrNullAsync(feed.Id, name, version, cancellationToken) == null
            && !await MirrorAsync(feed, name, version, cancellationToken))
        {
            return null;
        }

        await _artifacts.AddDownloadAsync(feed.Id, name, version, cancellationToken);

        return await _storage.GetArchiveStreamAsync(feed.Slug, name, version, cancellationToken);
    }

    public async Task<CargoPublishResult> PublishAsync(Feed feed, CargoPublishRequest request, CancellationToken cancellationToken)
    {
        var metadata = request.Metadata;
        var publishedName = GetString(metadata, "name");
        if (!CargoPackageName.IsValid(publishedName))
        {
            return Invalid(null, null,
                "The crate name must be ASCII letters, digits, '-' and '_', start with a letter, " +
                $"and have at most {CargoPackageName.MaxLength} characters.");
        }

        if (!CargoVersion.TryParse(GetString(metadata, "vers"), out var version))
        {
            return Invalid(publishedName, null, "The version of the crate must be a semantic version, e.g. 1.2.3.");
        }

        var name = publishedName.ToLowerInvariant();
        var versionText = version.ToString();

        JsonObject manifest;
        string readme;
        try
        {
            manifest = CargoIndexEntry.FromPublishMetadata(metadata);

            request.Crate.Position = 0;
            var crateReadme = await CargoCrateReader.ReadAsync(request.Crate, publishedName, versionText, cancellationToken);

            // Cargo sends the readme named in Cargo.toml; README.md of the crate is the fallback.
            readme = GetString(metadata, "readme") ?? crateReadme;
        }
        catch (InvalidCargoPackageException e)
        {
            LogInvalidCrate(e, feed.Slug);

            return Invalid(publishedName, versionText, e.Message);
        }

        var existingVersions = await _artifacts.FindVersionsAsync(feed.Id, name, cancellationToken);

        // Like crates.io, a crate keeps the name it was first published under, so its index lines agree.
        var existingName = existingVersions.Count == 0 ? null : CargoManifestSummary.Parse(existingVersions[0].MetadataJson).Name;
        if (existingName != null && existingName != publishedName)
        {
            return Invalid(publishedName, versionText, $"The crate is named {existingName} in this feed, not {publishedName}.");
        }

        // Versions that differ only in their build metadata are the same version (crates.io rejects them too).
        var existing = existingVersions.FirstOrDefault(v =>
            CargoVersion.TryParse(v.Version, out var other)
            && string.Equals(other.Precedence, version.Precedence, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            var allowOverwrites = _feedSettings.GetAllowPackageOverwrites(feed);
            if (allowOverwrites == PackageOverwriteAllowed.False
                || (allowOverwrites == PackageOverwriteAllowed.PrereleaseOnly && !version.IsPrerelease))
            {
                return AlreadyExists(publishedName, existing.Version);
            }

            await _artifacts.HardDeleteVersionAsync(feed.Id, name, existing.Version, cancellationToken);
            await _storage.DeleteAsync(feed.Slug, name, existing.Version, cancellationToken);
        }

        var result = await IndexAsync(
            feed,
            name,
            version,
            manifest.ToJsonString(),
            Truncate(GetString(metadata, "description")?.Trim(), MaxDescriptionLength),
            readme,
            request.Crate,
            cachedFrom: null,
            cancellationToken);

        if (result == ArtifactAddResult.AlreadyExists)
        {
            return AlreadyExists(publishedName, versionText);
        }

        LogPublished(publishedName, versionText, feed.Slug);

        return new CargoPublishResult { Status = CargoPublishStatus.Success, Name = publishedName, Version = versionText };
    }

    public async Task<bool> SetYankedAsync(Feed feed, string name, string version, bool yanked, CancellationToken cancellationToken)
    {
        if (!CargoPackageName.IsValid(name) || string.IsNullOrEmpty(version))
        {
            return false;
        }

        return await _artifacts.SetListedAsync(feed.Id, name.ToLowerInvariant(), version, listed: !yanked, cancellationToken);
    }

    /// <summary>
    /// Stores a version from the feed's mirrors in the feed, unless it is there already.
    /// </summary>
    /// <returns>True if the feed has the version afterwards.</returns>
    private async Task<bool> MirrorAsync(Feed feed, string name, string version, CancellationToken cancellationToken)
    {
        using var mirrorLock = await _mirrorLock.AcquireAsync(feed.Id, name, version, cancellationToken);

        // A concurrent request may have mirrored the version while we waited for the lock.
        if (await _artifacts.FindVersionOrNullAsync(feed.Id, name, version, cancellationToken) != null)
        {
            return true;
        }

        var upstream = (await _upstream.ListVersionsAsync(feed, name, cancellationToken))
            .FirstOrDefault(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));

        if (upstream == null || !CargoVersion.TryParse(upstream.Version, out var parsedVersion))
        {
            return false;
        }

        try
        {
            using var crate = await _upstream.DownloadCrateOrNullAsync(feed, upstream, cancellationToken);
            if (crate == null)
            {
                return false;
            }

            // The readme is only shown in the web UI, so a mirrored crate without a readable one is still served.
            string readme = null;
            try
            {
                readme = await CargoCrateReader.ReadAsync(crate, upstream.Name, upstream.Version, cancellationToken);
            }
            catch (InvalidCargoPackageException e)
            {
                LogMirroredCrateUnreadable(e, name, version);
            }

            var result = await IndexAsync(
                feed,
                name,
                parsedVersion,
                upstream.IndexLineJson,
                description: null,
                readme,
                crate,
                cachedFrom: upstream.Mirror.PackageSource.AbsoluteUri,
                cancellationToken,
                expectedHash: upstream.Cksum,
                listed: !upstream.Yanked);

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
        string name,
        CargoVersion version,
        string manifestJson,
        string description,
        string readme,
        Stream crate,
        string cachedFrom,
        CancellationToken cancellationToken,
        string expectedHash = null,
        bool listed = true)
    {
        crate.Position = 0;
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(crate, cancellationToken));

        if (expectedHash != null && !string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The crate {name} {version} has the SHA-256 {hash}, but the upstream lists {expectedHash}.");
        }

        crate.Position = 0;
        using var readmeStream = readme == null ? null : new MemoryStream(Encoding.UTF8.GetBytes(readme));

        await _storage.SaveAsync(feed.Slug, name, version.ToString(), crate, readmeStream, cancellationToken);

        return await _artifacts.AddVersionAsync(
            feed.Id,
            name,
            new ArtifactVersion
            {
                Version = version.ToString(),
                IsPrerelease = version.IsPrerelease,
                Listed = listed,
                Description = description,
                MetadataJson = manifestJson,
                ContentHash = hash,
                Size = crate.Length,
                HasReadme = readme != null,
                CachedFrom = cachedFrom,
                Published = _time.UtcNow,
            },
            cancellationToken);
    }

    private static string GetString(JsonObject item, string property)
    {
        return item[property] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrEmpty(text)
            ? text
            : null;
    }

    private static string Truncate(string text, int maxLength)
    {
        return text?.Length > maxLength ? text[..maxLength] : text;
    }

    private static CargoPublishResult Invalid(string name, string version, string message)
    {
        return new CargoPublishResult { Status = CargoPublishStatus.InvalidPackage, Name = name, Version = version, Message = message };
    }

    private static CargoPublishResult AlreadyExists(string name, string version)
    {
        return new CargoPublishResult
        {
            Status = CargoPublishStatus.AlreadyExists,
            Name = name,
            Version = version,
            Message = $"Version {version} of crate {name} already exists.",
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A crate published to feed {FeedSlug} is invalid")]
    private partial void LogInvalidCrate(Exception exception, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published crate {PackageName} {PackageVersion} to feed {FeedSlug}")]
    private partial void LogPublished(string packageName, string packageVersion, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mirrored crate {PackageName} {PackageVersion} from {PackageSource} with result {Result}")]
    private partial void LogMirrored(string packageName, string packageVersion, Uri packageSource, ArtifactAddResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mirrored crate {PackageName} {PackageVersion} could not be read, storing it without a readme")]
    private partial void LogMirroredCrateUnreadable(Exception exception, string packageName, string packageVersion);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror crate {PackageName} {PackageVersion}")]
    private partial void LogMirrorFailed(Exception exception, string packageName, string packageVersion);
}
