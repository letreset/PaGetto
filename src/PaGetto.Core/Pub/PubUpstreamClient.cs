using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Pub;

/// <summary>
/// Reads packages from the mirrors of a pub feed, which are other pub servers such as
/// <c>https://pub.dev</c>. The mirrors are tried in order, and the first one that has a package
/// serves all of its versions.
/// </summary>
public partial class PubUpstreamClient
{
    private const string PubMediaType = "application/vnd.pub.v2+json";

    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IUpstreamClientFactory _clients;
    private readonly UpstreamListingCache _cache;
    private readonly ILogger<PubUpstreamClient> _logger;

    public PubUpstreamClient(
        IFeedSettingsResolver feedSettings,
        IUpstreamClientFactory clients,
        UpstreamListingCache cache,
        ILogger<PubUpstreamClient> logger)
    {
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lists the versions of a package on the feed's mirrors. The listing is cached like the
    /// listings of NuGet mirrors, for the feed's upstream listing cache duration.
    /// </summary>
    /// <returns>The versions, or an empty list if the feed has no mirrors or none has the package.</returns>
    public virtual async Task<IReadOnlyList<PubUpstreamVersion>> ListVersionsAsync(Feed feed, string name, CancellationToken cancellationToken)
    {
        var mirrors = _feedSettings.GetMirrorOptions(feed);
        if (mirrors.Count == 0)
        {
            return [];
        }

        var cacheDuration = _feedSettings.GetUpstreamListingCacheDuration(feed);
        if (cacheDuration <= TimeSpan.Zero)
        {
            return await ListFromMirrorsAsync(mirrors, feed.Id, name, cancellationToken);
        }

        // Saving a feed (including its mirrors) updates UpdatedAtUtc, which invalidates its entries.
        var key = ("pub-versions", feed.Id, feed.UpdatedAtUtc.Ticks, name);

        return await _cache.GetOrListAsync(
            key,
            cacheDuration,
            token => ListFromMirrorsAsync(mirrors, feed.Id, name, token),
            cancellationToken);
    }

    /// <summary>
    /// Downloads a version's archive to a temporary file.
    /// </summary>
    /// <returns>The archive, or null if the upstream does not serve it.</returns>
    public virtual async Task<Stream> DownloadArchiveOrNullAsync(Feed feed, PubUpstreamVersion version, CancellationToken cancellationToken)
    {
        // Archives may be served from another host than the mirror, which must not get its credentials.
        var isMirrorUrl = version.ArchiveUrl.StartsWith(GetBaseUrl(version.Mirror), StringComparison.OrdinalIgnoreCase);

        using var client = _clients.CreateHttpClient(version.Mirror, feed.Id, authenticate: isMirrorUrl);
        using var response = await client.GetAsync(version.ArchiveUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            LogArchiveDownloadFailed(version.ArchiveUrl, (int)response.StatusCode);
            return null;
        }

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await content.AsTemporaryFileStreamAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<PubUpstreamVersion>> ListFromMirrorsAsync(
        IReadOnlyList<MirrorOptions> mirrors,
        Guid feedId,
        string name,
        CancellationToken cancellationToken)
    {
        foreach (var mirror in mirrors)
        {
            try
            {
                var versions = await ListFromMirrorAsync(mirror, feedId, name, cancellationToken);
                if (versions.Count > 0)
                {
                    return versions;
                }
            }
            catch (Exception e) when (e is HttpRequestException or JsonException
                || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // A mirror that is down or answers with something else is skipped, like a NuGet mirror.
                LogListingFailed(e, name, mirror.PackageSource);
            }
        }

        return [];
    }

    private async Task<IReadOnlyList<PubUpstreamVersion>> ListFromMirrorAsync(
        MirrorOptions mirror,
        Guid feedId,
        string name,
        CancellationToken cancellationToken)
    {
        using var client = _clients.CreateHttpClient(mirror, feedId, authenticate: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{GetBaseUrl(mirror)}api/packages/{Uri.EscapeDataString(name)}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(PubMediaType));

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        var versions = new List<PubUpstreamVersion>();
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("versions", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return versions;
        }

        foreach (var item in items.EnumerateArray())
        {
            var version = GetStringOrNull(item, "version");
            var archiveUrl = GetStringOrNull(item, "archive_url");

            // Versions this server could not store or download are left out.
            if (!PubVersion.TryParse(version, out _)
                || !Uri.TryCreate(archiveUrl, UriKind.Absolute, out var archiveUri)
                || (archiveUri.Scheme != Uri.UriSchemeHttp && archiveUri.Scheme != Uri.UriSchemeHttps)
                || !item.TryGetProperty("pubspec", out var pubspec)
                || pubspec.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            versions.Add(new PubUpstreamVersion
            {
                Version = version,
                ArchiveUrl = archiveUrl,
                ArchiveSha256 = GetStringOrNull(item, "archive_sha256"),
                PubspecJson = pubspec.GetRawText(),
                Retracted = item.TryGetProperty("retracted", out var retracted) && retracted.ValueKind == JsonValueKind.True,
                Published = DateTime.TryParse(
                    GetStringOrNull(item, "published"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var published)
                    ? published
                    : null,
                Mirror = mirror,
            });
        }

        return versions;
    }

    /// <summary>
    /// The mirror's hosted URL with a trailing slash, so it can be used as a prefix.
    /// </summary>
    private static string GetBaseUrl(MirrorOptions mirror)
    {
        var url = mirror.PackageSource.AbsoluteUri;
        return url.EndsWith('/') ? url : url + "/";
    }

    private static string GetStringOrNull(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to list the versions of pub package {PackageName} on upstream {PackageSource}")]
    private partial void LogListingFailed(Exception exception, string packageName, Uri packageSource);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream answered {StatusCode} for pub archive {ArchiveUrl}")]
    private partial void LogArchiveDownloadFailed(string archiveUrl, int statusCode);
}
