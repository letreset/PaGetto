using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Vsix;

/// <summary>
/// Reads extensions from the mirrors of a VSIX feed, which are the Atom feeds of other private
/// galleries: another VSIX feed, a gallery made with PrivateGalleryCreator, or
/// <c>https://www.vsixgallery.com/feed/</c>. A mirror's URL is the URL of its feed. The mirrors are
/// tried in order: the first one whose feed has an extension serves it. The files and images an
/// entry points to are fetched with the mirror's credentials only when they are on the mirror's host.
/// </summary>
public partial class VsixUpstreamClient
{
    private const long MaxFeedBytes = 64L * 1024 * 1024;
    private const long MaxImageBytes = 4L * 1024 * 1024;

    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IUpstreamClientFactory _clients;
    private readonly UpstreamListingCache _cache;
    private readonly ILogger<VsixUpstreamClient> _logger;

    public VsixUpstreamClient(
        IFeedSettingsResolver feedSettings,
        IUpstreamClientFactory clients,
        UpstreamListingCache cache,
        ILogger<VsixUpstreamClient> logger)
    {
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The extensions of the feed's mirrors, by lowercased identifier. An extension on several
    /// mirrors is the first mirror's. Each mirror's feed is cached for the feed's upstream listing
    /// cache duration.
    /// </summary>
    public virtual async Task<IReadOnlyDictionary<string, VsixUpstreamEntry>> GetIndexAsync(Feed feed, CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, VsixUpstreamEntry>(StringComparer.Ordinal);
        foreach (var mirror in _feedSettings.GetMirrorOptions(feed))
        {
            // One entry per mirror, so the cache counts the whole feed as one listing. It is kept
            // once per feed, and only for the listing cache duration.
            var index = await CachedAsync<IReadOnlyDictionary<string, VsixUpstreamEntry>>(
                feed,
                ("vsix-feed", feed.Id, feed.UpdatedAtUtc.Ticks, mirror.PackageSource.AbsoluteUri),
                async token => await TryReadFeedAsync(mirror, feed.Id, token) is { } read ? [read] : [],
                cancellationToken);

            if (index.Count == 0)
            {
                continue;
            }

            foreach (var (key, entry) in index[0])
            {
                merged.TryAdd(key, entry);
            }
        }

        return merged;
    }

    /// <summary>
    /// Downloads the VSIX file of an entry to a temporary file.
    /// </summary>
    /// <returns>The file, or null if it can't be downloaded.</returns>
    public virtual async Task<ArtifactFileContent> DownloadOrNullAsync(Feed feed, VsixUpstreamEntry entry, long maxBytes, CancellationToken cancellationToken)
    {
        var url = entry.Entry.DownloadUrl;

        try
        {
            using var client = CreateClient(feed, entry.Mirror, url);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogUpstreamFailed(url, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await ArtifactFileContent.ReadAsync(stream, maxBytes, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or ArtifactFileTooLargeException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogRequestFailed(e, url);
            return null;
        }
    }

    /// <summary>
    /// Fetches an image of an entry, which is passed through rather than stored.
    /// </summary>
    /// <returns>The image, or null if the entry has none or it can't be fetched.</returns>
    public virtual async Task<VsixImage> GetImageOrNullAsync(Feed feed, VsixUpstreamEntry entry, VsixImageKind kind, CancellationToken cancellationToken)
    {
        var url = kind == VsixImageKind.Icon ? entry.Entry.IconUrl : entry.Entry.PreviewImageUrl;
        if (url == null)
        {
            return null;
        }

        try
        {
            using var client = CreateClient(feed, entry.Mirror, url);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogUpstreamFailed(url, (int)response.StatusCode);
                return null;
            }

            // Only the image types this server serves from its own extensions, by the URL or the answer.
            var contentType = VsixArchiveReader.GetImageContentType(new Uri(url).AbsolutePath)
                ?? VsixArchiveReader.GetImageContentType(response.Content.Headers.ContentType?.MediaType switch
                {
                    "image/png" => ".png",
                    "image/jpeg" => ".jpg",
                    "image/gif" => ".gif",
                    "image/bmp" => ".bmp",
                    "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
                    _ => null,
                });
            if (contentType == null)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var limited = new LimitedReadStream(stream, MaxImageBytes, () => new InvalidDataException("The image is too large."));
            using var image = new MemoryStream();
            await limited.CopyToAsync(image, cancellationToken);

            return new VsixImage { Content = image.ToArray(), ContentType = contentType };
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogRequestFailed(e, url);
            return null;
        }
    }

    /// <summary>
    /// Reads a mirror's gallery feed.
    /// </summary>
    /// <returns>The newest entry of each extension, or null if the mirror is down or answers with something else.</returns>
    private async Task<Dictionary<string, VsixUpstreamEntry>> TryReadFeedAsync(MirrorOptions mirror, Guid feedId, CancellationToken cancellationToken)
    {
        var url = mirror.PackageSource.AbsoluteUri;

        try
        {
            using var client = _clients.CreateHttpClient(mirror, feedId, authenticate: true);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogUpstreamFailed(url, (int)response.StatusCode);
                return null;
            }

            // Read whole first, since XmlReader reads synchronously.
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var body = new MemoryStream();
            await using (var limited = new LimitedReadStream(
                content,
                MaxFeedBytes,
                () => new InvalidDataException($"The gallery feed is larger than {MaxFeedBytes / 1024 / 1024} MiB.")))
            {
                await limited.CopyToAsync(body, cancellationToken);
            }

            body.Position = 0;
            var index = new Dictionary<string, VsixUpstreamEntry>(StringComparer.Ordinal);
            foreach (var entry in VsixAtomFeed.Read(body, response.RequestMessage?.RequestUri ?? mirror.PackageSource))
            {
                // The reader only returns entries with a valid version.
                if (!VsixVersion.TryParse(entry.Manifest.Version, out var version))
                {
                    continue;
                }

                var key = VsixExtensionId.Normalize(entry.Manifest.Id);
                if (!index.TryGetValue(key, out var existing) || version.CompareTo(existing.Version) > 0)
                {
                    index[key] = new VsixUpstreamEntry { Entry = entry, Version = version, Mirror = mirror };
                }
            }

            return index;
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or XmlException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A mirror that is down or answers with something else is skipped, like a NuGet mirror.
            LogRequestFailed(e, url);
            return null;
        }
    }

    /// <summary>
    /// A client for a URL of a mirror's feed, with the mirror's credentials only when the URL is on the mirror's host.
    /// </summary>
    private HttpClient CreateClient(Feed feed, MirrorOptions mirror, string url)
    {
        var target = new Uri(url);
        var onMirrorHost = target.Scheme == mirror.PackageSource.Scheme && target.Authority == mirror.PackageSource.Authority;

        return _clients.CreateHttpClient(mirror, feed.Id, authenticate: onMirrorHost);
    }

    private async Task<IReadOnlyList<T>> CachedAsync<T>(
        Feed feed,
        object key,
        Func<CancellationToken, Task<IReadOnlyList<T>>> list,
        CancellationToken cancellationToken)
    {
        var cacheDuration = _feedSettings.GetUpstreamListingCacheDuration(feed);
        if (cacheDuration <= TimeSpan.Zero)
        {
            return await list(cancellationToken);
        }

        // Saving a feed (including its mirrors) updates UpdatedAtUtc, which is part of the keys.
        return await _cache.GetOrListAsync(key, cacheDuration, list, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream answered {StatusCode} for {UpstreamUrl}")]
    private partial void LogUpstreamFailed(string upstreamUrl, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read {UpstreamUrl}")]
    private partial void LogRequestFailed(Exception exception, string upstreamUrl);
}
