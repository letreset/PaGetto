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

namespace PaGetto.Core.Npm;

/// <summary>
/// Reads packages from the mirrors of an npm feed, which are other npm registries such as
/// <c>https://registry.npmjs.org</c>. The mirrors are tried in order, and the first one that has a
/// package serves all of its versions.
/// </summary>
public partial class NpmUpstreamClient
{
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IUpstreamClientFactory _clients;
    private readonly UpstreamListingCache _cache;
    private readonly ILogger<NpmUpstreamClient> _logger;

    public NpmUpstreamClient(
        IFeedSettingsResolver feedSettings,
        IUpstreamClientFactory clients,
        UpstreamListingCache cache,
        ILogger<NpmUpstreamClient> logger)
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
    /// <param name="feed">The feed whose mirrors are asked.</param>
    /// <param name="name">The package name as the client sent it.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The versions, or an empty list if the feed has no mirrors or none has the package.</returns>
    public virtual async Task<IReadOnlyList<NpmUpstreamVersion>> ListVersionsAsync(Feed feed, string name, CancellationToken cancellationToken)
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
        var key = ("npm-versions", feed.Id, feed.UpdatedAtUtc.Ticks, NpmPackageName.Normalize(name));

        return await _cache.GetOrListAsync(
            key,
            cacheDuration,
            token => ListFromMirrorsAsync(mirrors, feed.Id, name, token),
            cancellationToken);
    }

    /// <summary>
    /// Downloads a version's tarball to a temporary file.
    /// </summary>
    /// <returns>The tarball, or null if the upstream does not serve it.</returns>
    public virtual async Task<Stream> DownloadTarballOrNullAsync(Feed feed, NpmUpstreamVersion version, CancellationToken cancellationToken)
    {
        // Tarballs may be served from another host than the mirror, which must not get its credentials.
        var isMirrorUrl = version.TarballUrl.StartsWith(GetBaseUrl(version.Mirror), StringComparison.OrdinalIgnoreCase);

        using var client = _clients.CreateHttpClient(version.Mirror, feed.Id, authenticate: isMirrorUrl);
        using var response = await client.GetAsync(version.TarballUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            LogTarballDownloadFailed(version.TarballUrl, (int)response.StatusCode);
            return null;
        }

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await content.AsTemporaryFileStreamAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<NpmUpstreamVersion>> ListFromMirrorsAsync(
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

    private async Task<IReadOnlyList<NpmUpstreamVersion>> ListFromMirrorAsync(
        MirrorOptions mirror,
        Guid feedId,
        string name,
        CancellationToken cancellationToken)
    {
        using var client = _clients.CreateHttpClient(mirror, feedId, authenticate: true);

        // The full document rather than the abbreviated one for installs: it has the descriptions,
        // links and publish times that the web UI shows.
        using var request = new HttpRequestMessage(HttpMethod.Get, GetBaseUrl(mirror) + NpmPackageName.Escape(name));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        var root = document.RootElement;
        var versions = new List<NpmUpstreamVersion>();
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("versions", out var items)
            || items.ValueKind != JsonValueKind.Object)
        {
            return versions;
        }

        var distTags = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("dist-tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
        {
            foreach (var tag in tags.EnumerateObject())
            {
                if (tag.Value.ValueKind == JsonValueKind.String)
                {
                    distTags[tag.Name] = tag.Value.GetString();
                }
            }
        }

        root.TryGetProperty("time", out var times);

        foreach (var item in items.EnumerateObject())
        {
            var manifest = item.Value;
            if (manifest.ValueKind != JsonValueKind.Object
                || !manifest.TryGetProperty("dist", out var dist)
                || dist.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var tarballUrl = GetStringOrNull(dist, "tarball");

            // Versions this server could not store or download are left out.
            if (!NpmVersion.TryParse(item.Name, out _)
                || !Uri.TryCreate(tarballUrl, UriKind.Absolute, out var tarballUri)
                || (tarballUri.Scheme != Uri.UriSchemeHttp && tarballUri.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            var deprecated = GetStringOrNull(manifest, "deprecated");

            versions.Add(new NpmUpstreamVersion
            {
                Version = item.Name,
                ManifestJson = manifest.GetRawText(),
                TarballUrl = tarballUrl,
                Integrity = GetStringOrNull(dist, "integrity"),
                Shasum = GetStringOrNull(dist, "shasum"),
                Deprecated = string.IsNullOrEmpty(deprecated) ? null : deprecated,
                Published = times.ValueKind == JsonValueKind.Object && DateTime.TryParse(
                    GetStringOrNull(times, item.Name),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var published)
                    ? published
                    : null,
                DistTags = distTags,
                Mirror = mirror,
            });
        }

        return versions;
    }

    /// <summary>
    /// The mirror's registry URL with a trailing slash, so it can be used as a prefix.
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to list the versions of npm package {PackageName} on upstream {PackageSource}")]
    private partial void LogListingFailed(Exception exception, string packageName, Uri packageSource);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream answered {StatusCode} for npm tarball {TarballUrl}")]
    private partial void LogTarballDownloadFailed(string tarballUrl, int statusCode);
}
