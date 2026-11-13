using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Cargo;

/// <summary>
/// Reads crates from the mirrors of a Cargo feed, which are other sparse registries such as
/// <c>https://index.crates.io</c>. The mirrors are tried in order, and the first one that has a
/// crate serves all of its versions.
/// See: https://doc.rust-lang.org/cargo/reference/registry-index.html#sparse-protocol
/// </summary>
public partial class CargoUpstreamClient
{
    // The markers of a download URL template. Without any, the URL is {dl}/{crate}/{version}/download.
    private static readonly string[] _downloadMarkers = ["{crate}", "{version}", "{prefix}", "{lowerprefix}", "{sha256-checksum}"];

    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IUpstreamClientFactory _clients;
    private readonly UpstreamListingCache _cache;
    private readonly ILogger<CargoUpstreamClient> _logger;

    public CargoUpstreamClient(
        IFeedSettingsResolver feedSettings,
        IUpstreamClientFactory clients,
        UpstreamListingCache cache,
        ILogger<CargoUpstreamClient> logger)
    {
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lists the versions of a crate on the feed's mirrors. The listing is cached like the listings
    /// of NuGet mirrors, for the feed's upstream listing cache duration.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The crate's lowercased name.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The versions, or an empty list if the feed has no mirrors or none has the crate.</returns>
    public virtual async Task<IReadOnlyList<CargoUpstreamVersion>> ListVersionsAsync(Feed feed, string name, CancellationToken cancellationToken)
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
        var key = ("cargo-versions", feed.Id, feed.UpdatedAtUtc.Ticks, name);

        return await _cache.GetOrListAsync(
            key,
            cacheDuration,
            token => ListFromMirrorsAsync(mirrors, feed.Id, name, token),
            cancellationToken);
    }

    /// <summary>
    /// Downloads a version's <c>.crate</c> file to a temporary file.
    /// </summary>
    /// <returns>The crate, or null if the upstream does not serve it.</returns>
    public virtual async Task<Stream> DownloadCrateOrNullAsync(Feed feed, CargoUpstreamVersion version, CancellationToken cancellationToken)
    {
        // Crates may be served from another host than the index (crates.io serves them from
        // static.crates.io), which must not get the mirror's credentials.
        var isMirrorUrl = version.DownloadUrl.StartsWith(GetBaseUrl(version.Mirror), StringComparison.OrdinalIgnoreCase);

        using var client = _clients.CreateHttpClient(version.Mirror, feed.Id, authenticate: isMirrorUrl);
        using var response = await client.GetAsync(version.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            LogCrateDownloadFailed(version.DownloadUrl, (int)response.StatusCode);
            return null;
        }

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await content.AsTemporaryFileStreamAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CargoUpstreamVersion>> ListFromMirrorsAsync(
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
            catch (Exception e) when (e is HttpRequestException or JsonException or InvalidDataException
                || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // A mirror that is down or answers with something else is skipped, like a NuGet mirror.
                LogListingFailed(e, name, mirror.PackageSource);
            }
        }

        return [];
    }

    private async Task<IReadOnlyList<CargoUpstreamVersion>> ListFromMirrorAsync(
        MirrorOptions mirror,
        Guid feedId,
        string name,
        CancellationToken cancellationToken)
    {
        using var client = _clients.CreateHttpClient(mirror, feedId, authenticate: true);
        var baseUrl = GetBaseUrl(mirror);

        using var response = await client.GetAsync(baseUrl + CargoPackageName.GetIndexPath(name), cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var lines = await response.Content.ReadAsStringAsync(cancellationToken);

        var downloadTemplate = await GetDownloadTemplateAsync(client, baseUrl, cancellationToken);

        var versions = new List<CargoUpstreamVersion>();
        foreach (var line in lines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var entry = JsonDocument.Parse(line);
            var root = entry.RootElement;

            var crateName = GetStringOrNull(root, "name");
            var version = GetStringOrNull(root, "vers");
            var cksum = GetStringOrNull(root, "cksum");

            // Versions this server could not store or check are left out.
            if (!string.Equals(crateName, name, StringComparison.OrdinalIgnoreCase)
                || !CargoVersion.TryParse(version, out _)
                || cksum is not { Length: 64 })
            {
                continue;
            }

            versions.Add(new CargoUpstreamVersion
            {
                Name = crateName,
                Version = version,
                Cksum = cksum.ToLowerInvariant(),
                Yanked = root.TryGetProperty("yanked", out var yanked) && yanked.ValueKind == JsonValueKind.True,
                IndexLineJson = line,
                DownloadUrl = GetDownloadUrl(downloadTemplate, crateName, version, cksum),
                Mirror = mirror,
            });
        }

        return versions;
    }

    /// <summary>
    /// The <c>dl</c> URL of the mirror's <c>config.json</c>.
    /// </summary>
    private static async Task<string> GetDownloadTemplateAsync(HttpClient client, string baseUrl, CancellationToken cancellationToken)
    {
        await using var content = await client.GetStreamAsync(baseUrl + "config.json", cancellationToken);
        using var config = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        var dl = config.RootElement.ValueKind == JsonValueKind.Object ? GetStringOrNull(config.RootElement, "dl") : null;
        if (!Uri.TryCreate(dl?.Split('{')[0], UriKind.Absolute, out var dlUri)
            || (dlUri.Scheme != Uri.UriSchemeHttp && dlUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidDataException("The config.json of the registry has no http or https dl URL.");
        }

        return dl;
    }

    private static string GetDownloadUrl(string template, string name, string version, string cksum)
    {
        var hasMarker = false;
        foreach (var marker in _downloadMarkers)
        {
            hasMarker |= template.Contains(marker, StringComparison.Ordinal);
        }

        // Like Cargo, the name and version are put in as they are; both are checked to be URL-safe.
        if (!hasMarker)
        {
            return $"{template.TrimEnd('/')}/{name}/{version}/download";
        }

        var prefix = CargoPackageName.GetPrefix(name);
        return template
            .Replace("{crate}", name, StringComparison.Ordinal)
            .Replace("{version}", version, StringComparison.Ordinal)
            .Replace("{prefix}", prefix, StringComparison.Ordinal)
            .Replace("{lowerprefix}", prefix.ToLowerInvariant(), StringComparison.Ordinal)
            .Replace("{sha256-checksum}", cksum, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mirror's index URL with a trailing slash, so it can be used as a prefix.
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to list the versions of crate {PackageName} on upstream {PackageSource}")]
    private partial void LogListingFailed(Exception exception, string packageName, Uri packageSource);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream answered {StatusCode} for crate {CrateUrl}")]
    private partial void LogCrateDownloadFailed(string crateUrl, int statusCode);
}
