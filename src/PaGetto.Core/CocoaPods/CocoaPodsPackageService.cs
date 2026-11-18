using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.CocoaPods;

public partial class CocoaPodsPackageService : ICocoaPodsPackageService
{
    private const long BytesPerMiB = 1024L * 1024;
    private const int MaxDescriptionLength = 4000;

    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly CocoaPodsUpstreamClient _upstream;
    private readonly PackageMirrorLock _mirrorLock;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly SystemTime _time;
    private readonly ILogger<CocoaPodsPackageService> _logger;

    public CocoaPodsPackageService(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        CocoaPodsUpstreamClient upstream,
        PackageMirrorLock mirrorLock,
        IFeedSettingsResolver feedSettings,
        SystemTime time,
        ILogger<CocoaPodsPackageService> logger)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _mirrorLock = mirrorLock ?? throw new ArgumentNullException(nameof(mirrorLock));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> GetIndexAsync(Feed feed, string[] shard, CancellationToken cancellationToken)
    {
        var pods = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        // Every version of the feed, listed or not, hides the mirror's version of the same text.
        var stored = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var name in await FindPodNamesAsync(feed, cancellationToken))
        {
            if (!CocoaPodsPodName.GetShard(name).SequenceEqual(shard))
            {
                continue;
            }

            var versions = await _artifacts.FindVersionsAsync(feed.Id, CocoaPodsPodName.Escape(name), cancellationToken);
            stored[name] = versions.Select(v => v.Version).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var listed = versions.Where(v => v.Listed).Select(v => v.Version).ToList();
            if (listed.Count > 0)
            {
                pods[name] = listed;
            }
        }

        foreach (var pod in await _upstream.ListShardAsync(feed, shard, cancellationToken))
        {
            var hidden = stored.GetValueOrDefault(pod.Name);
            var versions = pod.Versions
                .Where(v => hidden?.Contains(v) != true)
                .Concat(pods.GetValueOrDefault(pod.Name) ?? [])
                .ToList();

            if (versions.Count > 0)
            {
                pods[pod.Name] = versions;
            }
        }

        return CocoaPodsSpecRepository.WriteIndex(
            pods.Select(p => KeyValuePair.Create(p.Key, (IReadOnlyList<string>)p.Value.Order(CocoaPodsVersion.StringComparer).ToList())));
    }

    public async Task<string> GetAllPodsAsync(Feed feed, CancellationToken cancellationToken)
    {
        var names = (await FindPodNamesAsync(feed, cancellationToken)).Order(StringComparer.Ordinal);

        return string.Concat(names.Select(n => n + "\n"));
    }

    public async Task<string> GetPodspecOrNullAsync(Feed feed, string name, string version, string sourcesUrl, CancellationToken cancellationToken)
    {
        if (!CocoaPodsPodName.IsValid(name) || !CocoaPodsVersion.TryParse(version, out _))
        {
            return null;
        }

        var stored = await _artifacts.FindVersionOrNullAsync(feed.Id, CocoaPodsPodName.Escape(name), version, cancellationToken)
            ?? await MirrorAsync(feed, name, version, cancellationToken);
        if (stored == null)
        {
            return null;
        }

        // A version published with its source archive gets the archive from this feed.
        if (stored.ContentHash != null && CocoaPodsPodspec.GetStoredArchiveType(stored.MetadataJson) is { } type)
        {
            var fileName = CocoaPodsPodspec.GetArchiveFileName(name, stored.Version, type);
            var url = $"{sourcesUrl}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(stored.Version)}/{Uri.EscapeDataString(fileName)}";

            return CocoaPodsPodspec.AddSourceUrl(stored.MetadataJson, url);
        }

        return stored.MetadataJson;
    }

    public async Task<Stream> GetSourceStreamOrNullAsync(Feed feed, string name, string version, string fileName, CancellationToken cancellationToken)
    {
        if (!CocoaPodsPodName.IsValid(name) || !CocoaPodsVersion.TryParse(version, out _))
        {
            return null;
        }

        var escaped = CocoaPodsPodName.Escape(name);
        var stored = await _artifacts.FindVersionOrNullAsync(feed.Id, escaped, version, cancellationToken);
        if (stored?.ContentHash == null
            || CocoaPodsPodspec.GetStoredArchiveType(stored.MetadataJson) is not { } type
            || !string.Equals(fileName, CocoaPodsPodspec.GetArchiveFileName(name, stored.Version, type), StringComparison.Ordinal))
        {
            return null;
        }

        await _artifacts.AddDownloadAsync(feed.Id, escaped, stored.Version, cancellationToken);

        return await _storage.GetArchiveStreamAsync(feed.Slug, escaped, stored.Version, cancellationToken);
    }

    public async Task<CocoaPodsPublishResult> PublishAsync(
        Feed feed,
        string podspec,
        Stream source,
        string sourceFileName,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        JsonObject spec;
        try
        {
            spec = CocoaPodsPodspec.Read(podspec);
        }
        catch (InvalidPodspecException e)
        {
            LogInvalidPodspec(e, feed.Slug);
            return Invalid(null, null, e.Message);
        }

        var name = CocoaPodsPodspec.GetName(spec);
        var text = CocoaPodsPodspec.GetVersion(spec);
        // Read checked the version.
        _ = CocoaPodsVersion.TryParse(text, out var version);

        string archiveType = null;
        if (source != null && !CocoaPodsPodspec.TryGetArchiveType(sourceFileName, out archiveType))
        {
            return Invalid(name, text, $"{sourceFileName} is not a source archive CocoaPods unpacks everywhere: a .zip, .tar.gz, .tgz, .tar.bz2, .tbz, .tar.xz, .txz or .tar file.");
        }

        if (source == null && spec["source"] is not JsonObject)
        {
            return Invalid(name, text, "The podspec has no source. Give it one, such as a git repository and tag, or publish its source archive with it.");
        }

        ArtifactFileContent archive = null;
        try
        {
            if (source != null)
            {
                try
                {
                    archive = await ArtifactFileContent.ReadAsync(source, maxBytes, cancellationToken);
                }
                catch (ArtifactFileTooLargeException)
                {
                    return new CocoaPodsPublishResult
                    {
                        Status = CocoaPodsPublishStatus.TooLarge,
                        Message = $"The source archive is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.",
                        Name = name,
                        Version = text,
                    };
                }

                CocoaPodsPodspec.SetStoredSource(spec, archiveType, archive.Sha256);
            }

            var escaped = CocoaPodsPodName.Escape(name);
            var existing = await _artifacts.FindVersionOrNullAsync(feed.Id, escaped, text, cancellationToken);
            if (existing != null)
            {
                if (!_feedSettings.CanOverwrite(feed, version.IsPrerelease))
                {
                    return AlreadyExists(name, existing.Version);
                }

                await _artifacts.HardDeleteVersionAsync(feed.Id, escaped, existing.Version, cancellationToken);
                await _storage.DeleteAsync(feed.Slug, escaped, existing.Version, cancellationToken);
            }

            var json = spec.ToJsonString();
            if (archive != null)
            {
                await _storage.SaveAsync(feed.Slug, escaped, text, archive.Stream, readme: null, cancellationToken);
            }

            var result = await AddVersionAsync(feed, escaped, version, json, CocoaPodsPodspec.GetSummary(spec), archive, cachedFrom: null, cancellationToken);
            if (result == ArtifactAddResult.AlreadyExists)
            {
                return AlreadyExists(name, text);
            }
        }
        finally
        {
            archive?.Dispose();
        }

        LogPublished(name, text, feed.Slug);

        return new CocoaPodsPublishResult { Status = CocoaPodsPublishStatus.Success, Name = name, Version = text };
    }

    /// <summary>
    /// The names of the pods stored in the feed, unescaped.
    /// </summary>
    private async Task<List<string>> FindPodNamesAsync(Feed feed, CancellationToken cancellationToken)
    {
        return (await _artifacts.FindNamesAsync(feed.Id, cancellationToken))
            .Select(escaped => CocoaPodsPodName.TryUnescape(escaped, out var name) ? name : null)
            .Where(name => name != null)
            .ToList();
    }

    /// <summary>
    /// Stores a version's podspec from the feed's mirrors in the feed, unless it is there already.
    /// </summary>
    /// <returns>The stored version, or null if no mirror has it.</returns>
    private async Task<ArtifactVersion> MirrorAsync(Feed feed, string name, string version, CancellationToken cancellationToken)
    {
        var pod = (await _upstream.ListShardAsync(feed, CocoaPodsPodName.GetShard(name), cancellationToken))
            .FirstOrDefault(p => p.Name == name);
        var listed = pod?.Versions.FirstOrDefault(v => v == version);
        if (listed == null || !CocoaPodsVersion.TryParse(listed, out var parsed))
        {
            return null;
        }

        var escaped = CocoaPodsPodName.Escape(name);
        using var mirrorLock = await _mirrorLock.AcquireAsync(feed.Id, escaped, listed, cancellationToken);

        // A concurrent request may have mirrored the version while we waited for the lock.
        var stored = await _artifacts.FindVersionOrNullAsync(feed.Id, escaped, listed, cancellationToken);
        if (stored != null)
        {
            return stored;
        }

        try
        {
            var maxBytes = _feedSettings.GetMaxPackageSizeBytes(feed);
            var podspec = await _upstream.GetPodspecOrNullAsync(feed, pod, listed, maxBytes, cancellationToken);
            if (podspec == null)
            {
                return null;
            }

            JsonObject spec;
            try
            {
                spec = CocoaPodsPodspec.Read(podspec);
            }
            catch (InvalidPodspecException e)
            {
                LogMirroredPodspecInvalid(e, name, listed);
                return null;
            }

            if (CocoaPodsPodspec.GetName(spec) != name || CocoaPodsPodspec.GetVersion(spec) != listed)
            {
                LogMirroredPodspecMismatch(name, listed, pod.Mirror.PackageSource);
                return null;
            }

            // The podspec is stored as the mirror serves it.
            var result = await AddVersionAsync(
                feed, escaped, parsed, podspec, CocoaPodsPodspec.GetSummary(spec), archive: null, pod.Mirror.PackageSource.AbsoluteUri, cancellationToken);

            LogMirrored(name, listed, pod.Mirror.PackageSource, result);

            // AlreadyExists means another instance stored the version meanwhile, so it is available.
            return await _artifacts.FindVersionOrNullAsync(feed.Id, escaped, listed, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogMirrorFailed(e, name, listed);

            return null;
        }
    }

    private async Task<ArtifactAddResult> AddVersionAsync(
        Feed feed,
        string escapedName,
        CocoaPodsVersion version,
        string podspecJson,
        string description,
        ArtifactFileContent archive,
        string cachedFrom,
        CancellationToken cancellationToken)
    {
        description = description?.Trim();

        return await _artifacts.AddVersionAsync(
            feed.Id,
            escapedName,
            new ArtifactVersion
            {
                Version = version.Text,
                IsPrerelease = version.IsPrerelease,
                Listed = true,
                Description = description?.Length > MaxDescriptionLength ? description[..MaxDescriptionLength] : description,
                MetadataJson = podspecJson,
                ContentHash = archive?.Sha256,
                Size = archive?.Size ?? Encoding.UTF8.GetByteCount(podspecJson),
                HasReadme = false,
                CachedFrom = cachedFrom,
                Published = _time.UtcNow,
            },
            cancellationToken);
    }

    private static CocoaPodsPublishResult Invalid(string name, string version, string message)
    {
        return new CocoaPodsPublishResult { Status = CocoaPodsPublishStatus.InvalidPackage, Message = message, Name = name, Version = version };
    }

    private static CocoaPodsPublishResult AlreadyExists(string name, string version)
    {
        return new CocoaPodsPublishResult
        {
            Status = CocoaPodsPublishStatus.AlreadyExists,
            Message = $"Version {version} of pod {name} already exists.",
            Name = name,
            Version = version,
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A podspec published to feed {FeedSlug} is invalid")]
    private partial void LogInvalidPodspec(Exception exception, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published pod {PackageName} {PackageVersion} to feed {FeedSlug}")]
    private partial void LogPublished(string packageName, string packageVersion, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mirrored pod {PackageName} {PackageVersion} from {PackageSource} with result {Result}")]
    private partial void LogMirrored(string packageName, string packageVersion, Uri packageSource, ArtifactAddResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mirrored podspec of {PackageName} {PackageVersion} is invalid")]
    private partial void LogMirroredPodspecInvalid(Exception exception, string packageName, string packageVersion);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mirrored podspec of {PackageName} {PackageVersion} from {PackageSource} names another pod or version")]
    private partial void LogMirroredPodspecMismatch(string packageName, string packageVersion, Uri packageSource);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror pod {PackageName} {PackageVersion}")]
    private partial void LogMirrorFailed(Exception exception, string packageName, string packageVersion);
}
