using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.VsCode;

/// <summary>
/// Reads extensions from the mirrors of a VS Code feed, which are other galleries of the gallery
/// API: Open VSX (<c>https://open-vsx.org/vscode/gallery</c>) or another VS Code feed. A mirror's
/// URL is the gallery's service URL, the <c>serviceUrl</c> of <c>product.json</c>. An extension is
/// read from the first mirror that has it; a search asks the first mirror that answers. The files a
/// mirror links to are fetched with the mirror's credentials only when they are on the mirror's host.
/// </summary>
public partial class VsCodeUpstreamClient
{
    private const long MaxQueryBytes = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IUpstreamClientFactory _clients;
    private readonly UpstreamListingCache _cache;
    private readonly ILogger<VsCodeUpstreamClient> _logger;

    public VsCodeUpstreamClient(
        IFeedSettingsResolver feedSettings,
        IUpstreamClientFactory clients,
        UpstreamListingCache cache,
        ILogger<VsCodeUpstreamClient> logger)
    {
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Finds an extension with all its versions on the feed's mirrors. Each mirror's answer is
    /// cached for the feed's upstream listing cache duration.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="id">The extension's identifier, <c>publisher.name</c>.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The extension of the first mirror that has it, or null.</returns>
    public virtual async Task<VsCodeUpstreamExtension> FindAsync(Feed feed, string id, CancellationToken cancellationToken)
    {
        return await FindAsync(
            feed,
            VsCodeGalleryApi.ExtensionNameFilter,
            VsCodeExtensionId.Normalize(id),
            e => string.Equals($"{e.Publisher.PublisherName}.{e.ExtensionName}", id, StringComparison.OrdinalIgnoreCase),
            cancellationToken);
    }

    /// <summary>
    /// Finds an extension by the UUID a mirror gives it, which VS Code asks for once it knows the
    /// extension. Each mirror's answer is cached for the feed's upstream listing cache duration.
    /// </summary>
    /// <returns>The extension of the first mirror that has it, or null.</returns>
    public virtual async Task<VsCodeUpstreamExtension> FindAsync(Feed feed, Guid uuid, CancellationToken cancellationToken)
    {
        return await FindAsync(feed, VsCodeGalleryApi.ExtensionIdFilter, uuid.ToString(), e => e.ExtensionId == uuid, cancellationToken);
    }

    private async Task<VsCodeUpstreamExtension> FindAsync(
        Feed feed,
        int filterType,
        string value,
        Func<VsCodeGalleryExtension, bool> matches,
        CancellationToken cancellationToken)
    {
        foreach (var mirror in _feedSettings.GetMirrorOptions(feed))
        {
            var query = new VsCodeGalleryQuery
            {
                Filters =
                [
                    new VsCodeGalleryQueryFilter
                    {
                        Criteria =
                        [
                            new VsCodeGalleryCriterion { FilterType = filterType, Value = value },
                            new VsCodeGalleryCriterion { FilterType = VsCodeGalleryApi.TargetFilter, Value = VsCodeGalleryApi.InstallationTarget },
                        ],
                        PageNumber = 1,
                        PageSize = 1,
                    },
                ],
                Flags = VsCodeGalleryApi.ReadExtensionFlags,
            };

            var found = await CachedAsync<VsCodeGalleryExtension>(
                feed,
                ("vscode-extension", feed.Id, feed.UpdatedAtUtc.Ticks, mirror.PackageSource.AbsoluteUri, filterType, value),
                async token => (IReadOnlyList<VsCodeGalleryExtension>)(await QueryOrNullAsync(feed, mirror, query, token))?.Extensions ?? [],
                cancellationToken);

            var extension = found.Count == 0 ? null : found[0];
            if (extension?.Publisher != null && matches(extension))
            {
                return new VsCodeUpstreamExtension { Extension = extension, Mirror = mirror };
            }
        }

        return null;
    }

    /// <summary>
    /// Passes a search on to the feed's mirrors. Answers are cached for the feed's upstream listing
    /// cache duration.
    /// </summary>
    /// <returns>The answer of the first mirror that answers, or null if none does.</returns>
    public virtual async Task<VsCodeGalleryResult> SearchAsync(Feed feed, VsCodeGalleryQuery query, CancellationToken cancellationToken)
    {
        var key = JsonSerializer.Serialize(query, _jsonOptions);
        foreach (var mirror in _feedSettings.GetMirrorOptions(feed))
        {
            var answer = await CachedAsync<VsCodeGalleryResult>(
                feed,
                ("vscode-search", feed.Id, feed.UpdatedAtUtc.Ticks, mirror.PackageSource.AbsoluteUri, key),
                async token => await QueryOrNullAsync(feed, mirror, query, token) is { } result ? [result] : [],
                cancellationToken);

            if (answer.Count > 0)
            {
                return answer[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Downloads a VSIX file that a mirror links to into a temporary file.
    /// </summary>
    /// <returns>The file, or null if it can't be downloaded.</returns>
    public virtual async Task<ArtifactFileContent> DownloadOrNullAsync(Feed feed, MirrorOptions mirror, string url, long maxBytes, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(feed, mirror, url);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                UpstreamListingCache.ReportFailure(response);
                LogUpstreamFailed(url, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await ArtifactFileContent.ReadAsync(stream, maxBytes, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or ArtifactFileTooLargeException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            UpstreamListingCache.ReportFailure();
            LogRequestFailed(e, url);
            return null;
        }
    }

    /// <summary>
    /// Fetches a file that a mirror links to, such as a readme, which is passed through rather than stored.
    /// </summary>
    /// <returns>The file, or null if it can't be fetched or is larger than <see cref="VsCodeArchiveReader.MaxAssetBytes"/>.</returns>
    public virtual async Task<VsCodeAsset> GetFileOrNullAsync(Feed feed, MirrorOptions mirror, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(feed, mirror, url);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                UpstreamListingCache.ReportFailure(response);
                LogUpstreamFailed(url, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var limited = new LimitedReadStream(stream, VsCodeArchiveReader.MaxAssetBytes, () => new InvalidDataException("The file is too large."));
            var content = new MemoryStream();
            await limited.CopyToAsync(content, cancellationToken);
            content.Position = 0;

            return new VsCodeAsset
            {
                Content = content,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? VsCodeArchiveReader.GetContentType(new Uri(url).AbsolutePath),
            };
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            UpstreamListingCache.ReportFailure();
            LogRequestFailed(e, url);
            return null;
        }
    }

    /// <summary>
    /// Posts a query to a mirror's gallery.
    /// </summary>
    /// <returns>The mirror's result, or null if the mirror is down or answers with something else.</returns>
    private async Task<VsCodeGalleryResult> QueryOrNullAsync(Feed feed, MirrorOptions mirror, VsCodeGalleryQuery query, CancellationToken cancellationToken)
    {
        var url = mirror.PackageSource.AbsoluteUri.TrimEnd('/') + "/extensionquery";

        try
        {
            using var client = _clients.CreateHttpClient(mirror, feed.Id, authenticate: true);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(query, options: _jsonOptions),
            };
            request.Headers.TryAddWithoutValidation("Accept", VsCodeGalleryApi.QueryAcceptHeader);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                UpstreamListingCache.ReportFailure(response);
                LogUpstreamFailed(url, (int)response.StatusCode);
                return null;
            }

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var limited = new LimitedReadStream(
                content,
                MaxQueryBytes,
                () => new InvalidDataException($"The answer of the gallery is larger than {MaxQueryBytes / 1024 / 1024} MiB."));
            var result = await JsonSerializer.DeserializeAsync<VsCodeGalleryQueryResult>(limited, _jsonOptions, cancellationToken);

            return result?.Results is { Count: > 0 } results ? results[0] : new VsCodeGalleryResult();
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or JsonException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            UpstreamListingCache.ReportFailure();
            // A mirror that is down or answers with something else is skipped, like a NuGet mirror.
            LogRequestFailed(e, url);
            return null;
        }
    }

    /// <summary>
    /// A client for a URL a mirror links to, with the mirror's credentials only when the URL is on the mirror's host.
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
