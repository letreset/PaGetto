using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.CocoaPods;

/// <summary>
/// Reads pods from the mirrors of a CocoaPods feed, which are spec repositories in the CDN format with
/// the shards of cdn.cocoapods.org, such as <c>https://cdn.cocoapods.org</c>. The mirrors are tried in
/// order: the first one whose index lists a pod lists its versions. Mirrors only have the podspecs;
/// the pods' sources stay where their podspecs point.
/// </summary>
public partial class CocoaPodsUpstreamClient
{
    // The most read for one index file or podspec. The index files of cdn.cocoapods.org are a few KiB.
    private const long MaxFileBytes = 16L * 1024 * 1024;

    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IUpstreamClientFactory _clients;
    private readonly UpstreamListingCache _cache;
    private readonly ILogger<CocoaPodsUpstreamClient> _logger;

    public CocoaPodsUpstreamClient(
        IFeedSettingsResolver feedSettings,
        IUpstreamClientFactory clients,
        UpstreamListingCache cache,
        ILogger<CocoaPodsUpstreamClient> logger)
    {
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lists the pods of a shard on the feed's mirrors, from their index files. A pod that several
    /// mirrors list has the versions of the first one. The listing is cached like the listings of
    /// NuGet mirrors, for the feed's upstream listing cache duration.
    /// </summary>
    /// <returns>The pods, or an empty list if the feed has no mirrors or none lists a pod of the shard.</returns>
    public virtual async Task<IReadOnlyList<CocoaPodsUpstreamPod>> ListShardAsync(Feed feed, IReadOnlyList<string> shard, CancellationToken cancellationToken)
    {
        var mirrors = _feedSettings.GetMirrorOptions(feed);
        if (mirrors.Count == 0)
        {
            return [];
        }

        var cacheDuration = _feedSettings.GetUpstreamListingCacheDuration(feed);
        if (cacheDuration <= TimeSpan.Zero)
        {
            return await ListFromMirrorsAsync(mirrors, feed.Id, shard, cancellationToken);
        }

        // Saving a feed (including its mirrors) updates UpdatedAtUtc, which invalidates its entries.
        var key = ("cocoapods-index", feed.Id, feed.UpdatedAtUtc.Ticks, string.Join('_', shard));

        return await _cache.GetOrListAsync(
            key,
            cacheDuration,
            token => ListFromMirrorsAsync(mirrors, feed.Id, shard, token),
            cancellationToken);
    }

    /// <summary>
    /// Downloads the podspec of a version from the mirror that lists the pod.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="pod">The pod, as listed by <see cref="ListShardAsync"/>.</param>
    /// <param name="version">The version, as the mirror lists it.</param>
    /// <param name="maxBytes">The largest podspec accepted.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The podspec, or null if the mirror does not have it or it can't be downloaded.</returns>
    public virtual async Task<string> GetPodspecOrNullAsync(Feed feed, CocoaPodsUpstreamPod pod, string version, long maxBytes, CancellationToken cancellationToken)
    {
        var path = CocoaPodsSpecRepository.GetPodspecPath(pod.Name, version);

        return await TryReadAsync(pod.Mirror, feed.Id, path, Math.Min(maxBytes, MaxFileBytes), cancellationToken);
    }

    private async Task<IReadOnlyList<CocoaPodsUpstreamPod>> ListFromMirrorsAsync(
        IReadOnlyList<MirrorOptions> mirrors,
        Guid feedId,
        IReadOnlyList<string> shard,
        CancellationToken cancellationToken)
    {
        var pods = new Dictionary<string, CocoaPodsUpstreamPod>(StringComparer.Ordinal);
        foreach (var mirror in mirrors)
        {
            var index = await TryReadAsync(mirror, feedId, CocoaPodsSpecRepository.GetIndexFileName(shard), MaxFileBytes, cancellationToken);
            if (index == null)
            {
                continue;
            }

            foreach (var (name, versions) in CocoaPodsSpecRepository.ReadIndex(index))
            {
                pods.TryAdd(name, new CocoaPodsUpstreamPod { Name = name, Versions = versions, Mirror = mirror });
            }
        }

        return pods.Values.ToList();
    }

    /// <summary>
    /// Reads a text file of a mirror.
    /// </summary>
    /// <returns>The file, or null if the mirror does not have it, is down, or answers with something else.</returns>
    private async Task<string> TryReadAsync(MirrorOptions mirror, Guid feedId, string path, long maxBytes, CancellationToken cancellationToken)
    {
        var baseUrl = mirror.PackageSource.AbsoluteUri;
        var url = (baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/") + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

        try
        {
            using var client = _clients.CreateHttpClient(mirror, feedId, authenticate: true);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                UpstreamListingCache.ReportFailure(response);
                LogUpstreamFailed(url, (int)response.StatusCode);
                return null;
            }

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var limited = new LimitedReadStream(
                content,
                maxBytes,
                () => new InvalidDataException($"The file is larger than {maxBytes} bytes."));
            using var reader = new StreamReader(limited);

            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            UpstreamListingCache.ReportFailure();
            // A mirror that is down or answers with something else is skipped, like a NuGet mirror.
            LogRequestFailed(e, url);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream answered {StatusCode} for {UpstreamUrl}")]
    private partial void LogUpstreamFailed(string upstreamUrl, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read {UpstreamUrl}")]
    private partial void LogRequestFailed(Exception exception, string upstreamUrl);
}
