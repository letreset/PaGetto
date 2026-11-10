using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

namespace PaGetto.Core.Pub;

public partial class PubPackageService : IPubPackageService
{
    // The length of the description column.
    private const int MaxDescriptionLength = 4000;

    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly PubUpstreamClient _upstream;
    private readonly PackageMirrorLock _mirrorLock;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly SystemTime _time;
    private readonly ILogger<PubPackageService> _logger;

    public PubPackageService(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        PubUpstreamClient upstream,
        PackageMirrorLock mirrorLock,
        IFeedSettingsResolver feedSettings,
        SystemTime time,
        ILogger<PubPackageService> logger)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _mirrorLock = mirrorLock ?? throw new ArgumentNullException(nameof(mirrorLock));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PubPackage> FindPackageOrNullAsync(Feed feed, string name, CancellationToken cancellationToken)
    {
        if (!PubPackageName.IsValid(name))
        {
            return null;
        }

        var versions = new Dictionary<string, PubPackageVersion>(StringComparer.OrdinalIgnoreCase);

        foreach (var upstream in await _upstream.ListVersionsAsync(feed, name, cancellationToken))
        {
            versions[upstream.Version] = new PubPackageVersion
            {
                Version = upstream.Version,
                IsPrerelease = PubVersion.TryParse(upstream.Version, out var parsed) && parsed.IsPrerelease,
                Retracted = upstream.Retracted,
                ArchiveSha256 = upstream.ArchiveSha256,
                PubspecJson = upstream.PubspecJson,
                Published = upstream.Published,
            };
        }

        foreach (var local in await _artifacts.FindVersionsAsync(feed.Id, name, cancellationToken))
        {
            versions[local.Version] = new PubPackageVersion
            {
                Version = local.Version,
                IsPrerelease = local.IsPrerelease,
                Retracted = !local.Listed,
                ArchiveSha256 = local.ContentHash,
                PubspecJson = local.MetadataJson,
                Published = local.Published,
            };
        }

        if (versions.Count == 0)
        {
            return null;
        }

        return new PubPackage(name, versions.Values.OrderBy(v => v.Version, PubVersion.StringComparer).ToList());
    }

    public async Task<Stream> GetArchiveStreamOrNullAsync(Feed feed, string name, string version, CancellationToken cancellationToken)
    {
        if (!PubPackageName.IsValid(name) || !PubVersion.TryParse(version, out _))
        {
            return null;
        }

        if (await _artifacts.FindVersionOrNullAsync(feed.Id, name, version, cancellationToken) == null
            && !await MirrorAsync(feed, name, version, cancellationToken))
        {
            return null;
        }

        await _artifacts.AddDownloadAsync(feed.Id, name, version, cancellationToken);

        return await _storage.GetArchiveStreamAsync(feed.Slug, name, version, cancellationToken);
    }

    public async Task<PubPublishResult> PublishAsync(Feed feed, Stream archive, CancellationToken cancellationToken)
    {
        PubArchive content;
        try
        {
            content = await PubArchiveReader.ReadAsync(archive, cancellationToken);
        }
        catch (InvalidPubArchiveException e)
        {
            LogInvalidArchive(e, feed.Slug);

            return new PubPublishResult { Status = PubPublishStatus.InvalidPackage, Message = e.Message };
        }

        var name = content.Name;
        var version = content.Version.ToString();

        if (await _artifacts.FindVersionOrNullAsync(feed.Id, name, version, cancellationToken) != null)
        {
            var allowOverwrites = _feedSettings.GetAllowPackageOverwrites(feed);
            if (allowOverwrites == PackageOverwriteAllowed.False
                || (allowOverwrites == PackageOverwriteAllowed.PrereleaseOnly && !content.Version.IsPrerelease))
            {
                return AlreadyExists(name, version);
            }

            await _artifacts.HardDeleteVersionAsync(feed.Id, name, version, cancellationToken);
            await _storage.DeleteAsync(feed.Slug, name, version, cancellationToken);
        }

        var result = await IndexAsync(
            feed,
            name,
            content.Version,
            content.Pubspec.ToJsonString(),
            content.Readme,
            archive,
            cachedFrom: null,
            published: _time.UtcNow,
            cancellationToken);

        if (result == ArtifactAddResult.AlreadyExists)
        {
            return AlreadyExists(name, version);
        }

        LogPublished(name, version, feed.Slug);

        return new PubPublishResult { Status = PubPublishStatus.Success, Name = name, Version = version };
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

        if (upstream == null || !PubVersion.TryParse(upstream.Version, out var parsedVersion))
        {
            return false;
        }

        try
        {
            using var archive = await _upstream.DownloadArchiveOrNullAsync(feed, upstream, cancellationToken);
            if (archive == null)
            {
                return false;
            }

            // The readme is only shown in the web UI, so a mirrored archive without a readable one is still served.
            string readme = null;
            try
            {
                readme = (await PubArchiveReader.ReadAsync(archive, cancellationToken)).Readme;
            }
            catch (InvalidPubArchiveException e)
            {
                LogMirroredArchiveUnreadable(e, name, version);
            }

            var result = await IndexAsync(
                feed,
                name,
                parsedVersion,
                upstream.PubspecJson,
                readme,
                archive,
                cachedFrom: upstream.Mirror.PackageSource.AbsoluteUri,
                published: upstream.Published ?? _time.UtcNow,
                cancellationToken,
                expectedHash: upstream.ArchiveSha256,
                listed: !upstream.Retracted);

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
        PubVersion version,
        string pubspecJson,
        string readme,
        Stream archive,
        string cachedFrom,
        DateTime published,
        CancellationToken cancellationToken,
        string expectedHash = null,
        bool listed = true)
    {
        archive.Position = 0;
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(archive, cancellationToken));

        if (expectedHash != null && !string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The archive of {name} {version} has the SHA-256 {hash}, but the upstream lists {expectedHash}.");
        }

        archive.Position = 0;
        using var readmeStream = readme == null ? null : new MemoryStream(Encoding.UTF8.GetBytes(readme));

        await _storage.SaveAsync(feed.Slug, name, version.ToString(), archive, readmeStream, cancellationToken);

        return await _artifacts.AddVersionAsync(
            feed.Id,
            name,
            new ArtifactVersion
            {
                Version = version.ToString(),
                IsPrerelease = version.IsPrerelease,
                Listed = listed,
                Description = GetDescription(pubspecJson),
                MetadataJson = pubspecJson,
                ContentHash = hash,
                Size = archive.Length,
                HasReadme = readme != null,
                CachedFrom = cachedFrom,
                Published = published,
            },
            cancellationToken);
    }

    private static string GetDescription(string pubspecJson)
    {
        try
        {
            var description = JsonNode.Parse(pubspecJson)?["description"] is JsonValue value && value.TryGetValue<string>(out var text)
                ? text.Trim()
                : null;

            return description?.Length > MaxDescriptionLength
                ? description[..MaxDescriptionLength]
                : description;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PubPublishResult AlreadyExists(string name, string version)
    {
        return new PubPublishResult
        {
            Status = PubPublishStatus.AlreadyExists,
            Name = name,
            Version = version,
            Message = $"Version {version} of package {name} already exists.",
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A pub package uploaded to feed {FeedSlug} is invalid")]
    private partial void LogInvalidArchive(Exception exception, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published pub package {PackageName} {PackageVersion} to feed {FeedSlug}")]
    private partial void LogPublished(string packageName, string packageVersion, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mirrored pub package {PackageName} {PackageVersion} from {PackageSource} with result {Result}")]
    private partial void LogMirrored(string packageName, string packageVersion, Uri packageSource, ArtifactAddResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mirrored archive of pub package {PackageName} {PackageVersion} could not be read, storing it without a readme")]
    private partial void LogMirroredArchiveUnreadable(Exception exception, string packageName, string packageVersion);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror pub package {PackageName} {PackageVersion}")]
    private partial void LogMirrorFailed(Exception exception, string packageName, string packageVersion);
}
