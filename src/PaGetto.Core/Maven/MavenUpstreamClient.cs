using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
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

namespace PaGetto.Core.Maven;

/// <summary>
/// Reads from the mirrors of a Maven feed, which are other Maven repositories such as
/// <c>https://repo.maven.apache.org/maven2</c>. The mirrors are tried in order, and the first one
/// that has a path serves it.
/// </summary>
public partial class MavenUpstreamClient
{
    // Metadata files are small; this bounds what a mirror can make the server read.
    private const long MaxMetadataBytes = 16 * 1024 * 1024;

    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IUpstreamClientFactory _clients;
    private readonly UpstreamListingCache _cache;
    private readonly ILogger<MavenUpstreamClient> _logger;

    public MavenUpstreamClient(
        IFeedSettingsResolver feedSettings,
        IUpstreamClientFactory clients,
        UpstreamListingCache cache,
        ILogger<MavenUpstreamClient> logger)
    {
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reads a <c>maven-metadata.xml</c> from the feed's mirrors. It is cached like the listings of
    /// NuGet mirrors, for the feed's upstream listing cache duration.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="path">The path of the metadata in the repository layout.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The metadata, or null if the feed has no mirrors or none has it.</returns>
    public virtual async Task<MavenMetadata> GetMetadataOrNullAsync(Feed feed, string path, CancellationToken cancellationToken)
    {
        var mirrors = _feedSettings.GetMirrorOptions(feed);
        if (mirrors.Count == 0)
        {
            return null;
        }

        var cacheDuration = _feedSettings.GetUpstreamListingCacheDuration(feed);
        if (cacheDuration <= TimeSpan.Zero)
        {
            return await GetMetadataFromMirrorsAsync(mirrors, feed.Id, path, cancellationToken) is [var uncached] ? uncached : null;
        }

        // Saving a feed (including its mirrors) updates UpdatedAtUtc, which invalidates its entries.
        var key = ("maven-metadata", feed.Id, feed.UpdatedAtUtc.Ticks, path);

        var cached = await _cache.GetOrListAsync(
            key,
            cacheDuration,
            token => GetMetadataFromMirrorsAsync(mirrors, feed.Id, path, token),
            cancellationToken);

        return cached is [var metadata] ? metadata : null;
    }

    /// <summary>
    /// Downloads a file from the first of the feed's mirrors that has it, to a temporary file. When
    /// the mirror has a SHA-1 checksum file for it, the file is checked against it.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="path">The path of the file in the repository layout.</param>
    /// <param name="maxBytes">The most the file may have.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The file, or null if no mirror has it.</returns>
    /// <exception cref="InvalidDataException">The file does not match the mirror's checksum.</exception>
    /// <exception cref="MavenFileTooLargeException">The file is larger than <paramref name="maxBytes"/>.</exception>
    public virtual async Task<MavenUpstreamFile> DownloadFileOrNullAsync(Feed feed, string path, long maxBytes, CancellationToken cancellationToken)
    {
        foreach (var mirror in _feedSettings.GetMirrorOptions(feed))
        {
            MavenFileContent content = null;
            try
            {
                using var client = _clients.CreateHttpClient(mirror, feed.Id, authenticate: true);
                using var response = await client.GetAsync(GetUrl(mirror, path), HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    LogDownloadFailed(path, mirror.PackageSource, (int)response.StatusCode);
                    continue;
                }

                await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    content = await MavenFileContent.ReadAsync(body, maxBytes, cancellationToken);
                }

                var expectedSha1 = await GetSha1OrNullAsync(client, GetUrl(mirror, path + ".sha1"), cancellationToken);
                if (expectedSha1 != null && !string.Equals(expectedSha1, content.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"The file {path} from {mirror.PackageSource} has the SHA-1 {content.Sha1}, but the mirror lists {expectedSha1}.");
                }

                var file = new MavenUpstreamFile
                {
                    Content = content,
                    Mirror = mirror,
                    LastModified = response.Content.Headers.LastModified?.UtcDateTime,
                };

                content = null;
                return file;
            }
            catch (Exception e) when (e is HttpRequestException
                || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // A mirror that is down is skipped, like a NuGet mirror.
                LogDownloadException(e, path, mirror.PackageSource);
            }
            finally
            {
                content?.Dispose();
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<MavenMetadata>> GetMetadataFromMirrorsAsync(
        IReadOnlyList<MirrorOptions> mirrors,
        Guid feedId,
        string path,
        CancellationToken cancellationToken)
    {
        foreach (var mirror in mirrors)
        {
            try
            {
                using var client = _clients.CreateHttpClient(mirror, feedId, authenticate: true);
                using var response = await client.GetAsync(GetUrl(mirror, path), HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    continue;
                }

                response.EnsureSuccessStatusCode();

                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                var limited = new LimitedReadStream(body, MaxMetadataBytes, () => new InvalidDataException("The metadata is too large."));

                return [MavenMetadataXml.Read(limited)];
            }
            catch (Exception e) when (e is HttpRequestException or XmlException or InvalidDataException
                || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // A mirror that is down or answers with something else is skipped, like a NuGet mirror.
                LogMetadataFailed(e, path, mirror.PackageSource);
            }
        }

        return [];
    }

    /// <returns>The SHA-1 the mirror lists for a file, or null if it lists none or can't be reached.</returns>
    private async Task<string> GetSha1OrNullAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            text = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogChecksumFailed(e, url);
            return null;
        }

        // The file is the hex checksum, sometimes followed by the file name.
        var checksum = text.Trim().Split((char[])null, 2, StringSplitOptions.RemoveEmptyEntries) is [var first, ..] ? first : null;

        return checksum?.Length == 40 ? checksum : null;
    }

    private static string GetUrl(MirrorOptions mirror, string path)
    {
        return mirror.PackageSource.AbsoluteUri.TrimEnd('/') + "/" + path;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read Maven metadata {Path} from upstream {PackageSource}")]
    private partial void LogMetadataFailed(Exception exception, string path, Uri packageSource);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream {PackageSource} answered {StatusCode} for Maven file {Path}")]
    private partial void LogDownloadFailed(string path, Uri packageSource, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read the checksum {Url}, the file is stored without checking it")]
    private partial void LogChecksumFailed(Exception exception, string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to download Maven file {Path} from upstream {PackageSource}")]
    private partial void LogDownloadException(Exception exception, string path, Uri packageSource);
}
