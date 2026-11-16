using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Composer;

/// <summary>
/// Reads packages from the mirrors of a Composer feed, which are Composer repositories that serve
/// their metadata at <c>p2/</c> under their URL, such as <c>https://repo.packagist.org</c>. The
/// mirrors are tried in order: the first one that has a package lists its versions. A version's
/// zip is downloaded from the dist URL of the mirror's metadata, which for Packagist is the
/// package's code host.
/// See: https://getcomposer.org/doc/05-repositories.md#metadata-url
/// </summary>
public partial class ComposerUpstreamClient
{
    // The most metadata read for one package. Packagist's largest are a few MiB.
    private const long MaxMetadataBytes = 64L * 1024 * 1024;

    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IUpstreamClientFactory _clients;
    private readonly UpstreamListingCache _cache;
    private readonly ILogger<ComposerUpstreamClient> _logger;

    public ComposerUpstreamClient(
        IFeedSettingsResolver feedSettings,
        IUpstreamClientFactory clients,
        UpstreamListingCache cache,
        ILogger<ComposerUpstreamClient> logger)
    {
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lists the versions of a package on the feed's mirrors: its tagged versions from
    /// <c>p2/{name}.json</c>, or its development versions from <c>p2/{name}~dev.json</c>. The
    /// listing is cached like the listings of NuGet mirrors, for the feed's upstream listing cache duration.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The package name, lowercased.</param>
    /// <param name="dev">Whether to list the development versions rather than the tagged ones.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The versions, or an empty list if the feed has no mirrors or none has the package.</returns>
    public virtual async Task<IReadOnlyList<ComposerUpstreamVersion>> ListVersionsAsync(Feed feed, string name, bool dev, CancellationToken cancellationToken)
    {
        var mirrors = _feedSettings.GetMirrorOptions(feed);
        if (mirrors.Count == 0)
        {
            return [];
        }

        var cacheDuration = _feedSettings.GetUpstreamListingCacheDuration(feed);
        if (cacheDuration <= TimeSpan.Zero)
        {
            return await ListFromMirrorsAsync(mirrors, feed.Id, name, dev, cancellationToken);
        }

        // Saving a feed (including its mirrors) updates UpdatedAtUtc, which invalidates its entries.
        var key = ("composer-versions", feed.Id, feed.UpdatedAtUtc.Ticks, name, dev);

        return await _cache.GetOrListAsync(
            key,
            cacheDuration,
            token => ListFromMirrorsAsync(mirrors, feed.Id, name, dev, token),
            cancellationToken);
    }

    /// <summary>
    /// Downloads the zip of a version from its dist URL, to a temporary file. The mirror's
    /// authentication is only sent when the URL is on the mirror's host.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="version">The version, as listed by <see cref="ListVersionsAsync"/>.</param>
    /// <param name="maxBytes">The largest zip accepted.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The zip, or null if the version has no zip dist or it can't be downloaded.</returns>
    public virtual async Task<ArtifactFileContent> DownloadDistOrNullAsync(
        Feed feed,
        ComposerUpstreamVersion version,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var metadata = JsonNode.Parse(version.Json) as JsonObject;
        if (!TryGetZipDist(metadata, out var url, out var shasum))
        {
            return null;
        }

        var mirrorUrl = version.Mirror.PackageSource;
        var onMirrorHost = url.Scheme == mirrorUrl.Scheme && url.Authority == mirrorUrl.Authority;

        try
        {
            using var client = _clients.CreateHttpClient(version.Mirror, feed.Id, authenticate: onMirrorHost);

            // Code hosts such as GitHub's API refuse requests without a user agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PaGetto");

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogUpstreamFailed(url.AbsoluteUri, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var zip = await ArtifactFileContent.ReadAsync(stream, maxBytes, cancellationToken);

            if (!string.IsNullOrEmpty(shasum) && !string.Equals(shasum, zip.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                zip.Dispose();
                LogChecksumMismatch(url.AbsoluteUri);
                return null;
            }

            return zip;
        }
        catch (Exception e) when (e is HttpRequestException or ArtifactFileTooLargeException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogRequestFailed(e, url.AbsoluteUri);
            return null;
        }
    }

    /// <summary>
    /// The URL and SHA-1 of a version's zip dist.
    /// </summary>
    /// <returns>False if the version has no dist, or one that is not a zip at an http or https URL.</returns>
    public static bool TryGetZipDist(JsonObject version, out Uri url, out string shasum)
    {
        url = null;
        shasum = null;

        if (version?["dist"] is not JsonObject dist
            || GetString(dist, "type") != ComposerManifest.ZipDistType
            || !Uri.TryCreate(GetString(dist, "url"), UriKind.Absolute, out url)
            || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
        {
            url = null;
            return false;
        }

        shasum = GetString(dist, "shasum");
        return true;
    }

    private async Task<IReadOnlyList<ComposerUpstreamVersion>> ListFromMirrorsAsync(
        IReadOnlyList<MirrorOptions> mirrors,
        Guid feedId,
        string name,
        bool dev,
        CancellationToken cancellationToken)
    {
        foreach (var mirror in mirrors)
        {
            var versions = await TryListAsync(mirror, feedId, name, dev, cancellationToken);
            if (versions?.Count > 0)
            {
                return versions;
            }
        }

        return [];
    }

    /// <summary>
    /// Reads a package's metadata from a mirror.
    /// </summary>
    /// <returns>The versions, or null if the mirror does not have the package, is down, or answers with something else.</returns>
    private async Task<List<ComposerUpstreamVersion>> TryListAsync(
        MirrorOptions mirror,
        Guid feedId,
        string name,
        bool dev,
        CancellationToken cancellationToken)
    {
        var baseUrl = mirror.PackageSource.AbsoluteUri;
        var url = $"{(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/")}p2/{name}{(dev ? "~dev" : "")}.json";

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
                LogUpstreamFailed(url, (int)response.StatusCode);
                return null;
            }

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var limited = new LimitedReadStream(
                content,
                MaxMetadataBytes,
                () => new InvalidDataException($"The metadata is larger than {MaxMetadataBytes / 1024 / 1024} MiB."));

            var metadata = await JsonNode.ParseAsync(limited, cancellationToken: cancellationToken) as JsonObject;
            if (metadata?["packages"] is not JsonObject packages || packages[name] is not JsonArray listed)
            {
                return null;
            }

            var minified = GetString(metadata, "minified") == "composer/2.0";
            var versions = new List<ComposerUpstreamVersion>();
            foreach (var item in minified ? Expand(listed) : listed.ToList())
            {
                // Versions Composer could not read are left out.
                if (item is JsonObject version
                    && ComposerVersion.TryParse(GetString(version, "version"), out var parsed)
                    && parsed.IsDev == dev)
                {
                    versions.Add(new ComposerUpstreamVersion
                    {
                        Version = parsed.Text,
                        NormalizedVersion = parsed.Normalized,
                        Json = version.ToJsonString(),
                        Mirror = mirror,
                    });
                }
            }

            return versions;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidDataException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A mirror that is down or answers with something else is skipped, like a NuGet mirror.
            LogRequestFailed(e, url);
            return null;
        }
    }

    /// <summary>
    /// Composer's <c>MetadataMinifier::expand</c>: each version of minified metadata only has the
    /// fields that differ from the version before it, and <c>__unset</c> for those it lacks.
    /// </summary>
    private static List<JsonNode> Expand(JsonArray versions)
    {
        var expanded = new List<JsonNode>();
        JsonObject current = null;

        foreach (var item in versions)
        {
            if (item is not JsonObject version)
            {
                continue;
            }

            current = current == null ? new JsonObject() : (JsonObject)current.DeepClone();
            foreach (var (key, value) in version)
            {
                if (value is JsonValue unset && unset.TryGetValue<string>(out var text) && text == "__unset")
                {
                    current.Remove(key);
                }
                else
                {
                    current[key] = value?.DeepClone();
                }
            }

            expanded.Add(current);
        }

        return expanded;
    }

    private static string GetString(JsonObject item, string property)
    {
        return item?[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream answered {StatusCode} for {UpstreamUrl}")]
    private partial void LogUpstreamFailed(string upstreamUrl, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read {UpstreamUrl}")]
    private partial void LogRequestFailed(Exception exception, string upstreamUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The zip at {UpstreamUrl} does not match the SHA-1 of the mirror's metadata")]
    private partial void LogChecksumMismatch(string upstreamUrl);
}
