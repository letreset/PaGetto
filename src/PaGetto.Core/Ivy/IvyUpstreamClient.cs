using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Maven;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Ivy;

/// <summary>
/// Reads from the mirrors of an Ivy feed, which are other Ivy repositories in the same layout, such as
/// <c>https://repo.scala-sbt.org/scalasbt/sbt-plugin-releases</c>. A mirror lists the revisions of a
/// module in the directory listing of its folder; the first mirror that lists a module serves its
/// revisions. Files are downloaded like those of a Maven mirror, from the first mirror that has them.
/// </summary>
public partial class IvyUpstreamClient
{
    // Directory listings are small; this bounds what a mirror can make the server read.
    private const long MaxListingBytes = 16L * 1024 * 1024;

    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IUpstreamClientFactory _clients;
    private readonly UpstreamListingCache _cache;
    private readonly MavenUpstreamClient _files;
    private readonly ILogger<IvyUpstreamClient> _logger;

    public IvyUpstreamClient(
        IFeedSettingsResolver feedSettings,
        IUpstreamClientFactory clients,
        UpstreamListingCache cache,
        MavenUpstreamClient files,
        ILogger<IvyUpstreamClient> logger)
    {
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lists the revisions of a module from the directory listing of its folder on the first of the
    /// feed's mirrors that lists any. It is cached like the listings of NuGet mirrors, for the feed's
    /// upstream listing cache duration.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="modulePath">The path of the module's folder, see <see cref="IvyPath.GetModulePath"/>.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The revisions. Empty if the feed has no mirrors or none lists the module.</returns>
    public virtual async Task<IReadOnlyList<string>> ListRevisionsAsync(Feed feed, string modulePath, CancellationToken cancellationToken)
    {
        var mirrors = _feedSettings.GetMirrorOptions(feed);
        if (mirrors.Count == 0)
        {
            return [];
        }

        var cacheDuration = _feedSettings.GetUpstreamListingCacheDuration(feed);
        if (cacheDuration <= TimeSpan.Zero)
        {
            return await ListFromMirrorsAsync(mirrors, feed.Id, modulePath, cancellationToken);
        }

        // Saving a feed (including its mirrors) updates UpdatedAtUtc, which invalidates its entries.
        var key = ("ivy-revisions", feed.Id, feed.UpdatedAtUtc.Ticks, modulePath);

        return await _cache.GetOrListAsync(
            key,
            cacheDuration,
            token => ListFromMirrorsAsync(mirrors, feed.Id, modulePath, token),
            cancellationToken);
    }

    /// <summary>
    /// Downloads a file from the first of the feed's mirrors that has it, checked against the mirror's
    /// SHA-1 checksum file when it has one.
    /// </summary>
    /// <returns>The file, or null if no mirror has it.</returns>
    /// <exception cref="InvalidDataException">The file does not match the mirror's checksum.</exception>
    /// <exception cref="ArtifactFileTooLargeException">The file is larger than <paramref name="maxBytes"/>.</exception>
    public virtual Task<MavenUpstreamFile> DownloadFileOrNullAsync(Feed feed, string path, long maxBytes, CancellationToken cancellationToken)
    {
        return _files.DownloadFileOrNullAsync(feed, path, maxBytes, cancellationToken);
    }

    /// <summary>
    /// The folders a directory listing links to, e.g. the revisions in the listing of a module's folder.
    /// Only links to direct children of the listing's URL count, so the parent folder and links
    /// elsewhere are left out.
    /// </summary>
    /// <param name="listingUrl">The URL of the listing, ending with a slash.</param>
    /// <param name="html">The listing.</param>
    public static IReadOnlyList<string> ParseFolders(Uri listingUrl, string html)
    {
        var folders = new List<string>();
        foreach (Match match in LinkRegex().Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            if (!Uri.TryCreate(listingUrl, href, out var target)
                || !target.AbsolutePath.StartsWith(listingUrl.AbsolutePath, StringComparison.Ordinal)
                || !string.Equals(target.Authority, listingUrl.Authority, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rest = target.AbsolutePath[listingUrl.AbsolutePath.Length..];
            if (rest.Length < 2 || !rest.EndsWith('/') || rest.IndexOf('/') != rest.Length - 1)
            {
                continue;
            }

            var folder = Uri.UnescapeDataString(rest[..^1]);
            if (!folders.Contains(folder, StringComparer.Ordinal))
            {
                folders.Add(folder);
            }
        }

        return folders;
    }

    private async Task<IReadOnlyList<string>> ListFromMirrorsAsync(
        IReadOnlyList<MirrorOptions> mirrors,
        Guid feedId,
        string modulePath,
        CancellationToken cancellationToken)
    {
        foreach (var mirror in mirrors)
        {
            var url = new Uri(mirror.PackageSource.AbsoluteUri.TrimEnd('/') + "/" + modulePath + "/");
            try
            {
                using var client = _clients.CreateHttpClient(mirror, feedId, authenticate: true);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    UpstreamListingCache.ReportFailure(response);
                    LogListingFailed(url, (int)response.StatusCode);
                    continue;
                }

                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(new LimitedReadStream(
                    body,
                    MaxListingBytes,
                    () => new InvalidDataException($"The listing is larger than {MaxListingBytes / 1024 / 1024} MiB.")));

                // The folder of a module also has the cross-version folders of its sbt plugins.
                var revisions = ParseFolders(url, await reader.ReadToEndAsync(cancellationToken))
                    .Where(f => !IvyPath.IsCrossVersionFolder(f))
                    .ToList();

                if (revisions.Count > 0)
                {
                    return revisions;
                }
            }
            catch (Exception e) when (e is HttpRequestException or InvalidDataException
                || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                UpstreamListingCache.ReportFailure();
                // A mirror that is down is skipped, like a NuGet mirror.
                LogListingException(e, url);
            }
        }

        return [];
    }

    [GeneratedRegex(@"<a\s[^>]*?href\s*=\s*[""'](?<href>[^""'#?]*)[""']", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream answered {StatusCode} for the Ivy listing {UpstreamUrl}")]
    private partial void LogListingFailed(Uri upstreamUrl, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read the Ivy listing {UpstreamUrl}")]
    private partial void LogListingException(Exception exception, Uri upstreamUrl);
}
