using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Hex;

/// <summary>
/// Reads packages from the mirrors of a Hex feed, which are other Hex repositories such as
/// <c>https://repo.hex.pm</c>. The mirrors are tried in order, and the first one that has a package
/// serves all of its versions. Their registry signatures are not checked: the feed signs what it
/// serves with its own key, and a tarball is stored only when it matches the checksum the mirror lists.
/// </summary>
public partial class HexUpstreamClient
{
    private const int ChecksumLength = 32;

    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IUpstreamClientFactory _clients;
    private readonly UpstreamListingCache _cache;
    private readonly ILogger<HexUpstreamClient> _logger;

    public HexUpstreamClient(
        IFeedSettingsResolver feedSettings,
        IUpstreamClientFactory clients,
        UpstreamListingCache cache,
        ILogger<HexUpstreamClient> logger)
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
    public virtual async Task<IReadOnlyList<HexUpstreamRelease>> ListReleasesAsync(Feed feed, string name, CancellationToken cancellationToken)
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
        var key = ("hex-releases", feed.Id, feed.UpdatedAtUtc.Ticks, name);

        return await _cache.GetOrListAsync(
            key,
            cacheDuration,
            token => ListFromMirrorsAsync(mirrors, feed.Id, name, token),
            cancellationToken);
    }

    /// <summary>
    /// Downloads a version's tarball to a temporary file, failing with an
    /// <see cref="Artifacts.ArtifactFileTooLargeException"/> once it has more than <paramref name="maxBytes"/>.
    /// </summary>
    /// <returns>The tarball, or null if the upstream does not serve it.</returns>
    public virtual async Task<Stream> DownloadTarballOrNullAsync(Feed feed, string name, HexUpstreamRelease release, long maxBytes, CancellationToken cancellationToken)
    {
        var url = $"{GetBaseUrl(release.Mirror)}tarballs/{name}-{Uri.EscapeDataString(release.Release.Version)}.tar";

        using var client = _clients.CreateHttpClient(release.Mirror, feed.Id, authenticate: true);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            UpstreamListingCache.ReportFailure(response);
            LogTarballDownloadFailed(url, (int)response.StatusCode);
            return null;
        }

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await content.AsTemporaryFileStreamAsync(maxBytes, cancellationToken);
    }

    private async Task<IReadOnlyList<HexUpstreamRelease>> ListFromMirrorsAsync(
        IReadOnlyList<MirrorOptions> mirrors,
        Guid feedId,
        string name,
        CancellationToken cancellationToken)
    {
        foreach (var mirror in mirrors)
        {
            try
            {
                var releases = await ListFromMirrorAsync(mirror, feedId, name, cancellationToken);
                if (releases.Count > 0)
                {
                    return releases;
                }
            }
            catch (Exception e) when (e is HttpRequestException or InvalidDataException
                || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                UpstreamListingCache.ReportFailure();
                // A mirror that is down or answers with something else is skipped, like a NuGet mirror.
                LogListingFailed(e, name, mirror.PackageSource);
            }
        }

        return [];
    }

    private async Task<IReadOnlyList<HexUpstreamRelease>> ListFromMirrorAsync(
        MirrorOptions mirror,
        Guid feedId,
        string name,
        CancellationToken cancellationToken)
    {
        using var client = _clients.CreateHttpClient(mirror, feedId, authenticate: true);
        using var response = await client.GetAsync(GetBaseUrl(mirror) + "packages/" + name, cancellationToken);

        // repo.hex.pm answers 403 for a package it does not have.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Gone)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var resource = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var releases = HexRegistry.ReadPackage(HexRegistry.ReadPayload(resource), name);

        // Versions this server could not store or check are left out.
        return releases
            .Where(r => HexVersion.TryParse(r.Version, out _)
                && r.InnerChecksum is { Length: ChecksumLength }
                && r.OuterChecksum is { Length: ChecksumLength })
            .Select(r => new HexUpstreamRelease { Release = r, Mirror = mirror })
            .ToList();
    }

    /// <summary>
    /// The mirror's URL with a trailing slash, so it can be used as a prefix.
    /// </summary>
    private static string GetBaseUrl(MirrorOptions mirror)
    {
        var url = mirror.PackageSource.AbsoluteUri;
        return url.EndsWith('/') ? url : url + "/";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to list the versions of Hex package {PackageName} on upstream {PackageSource}")]
    private partial void LogListingFailed(Exception exception, string packageName, Uri packageSource);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream answered {StatusCode} for Hex tarball {TarballUrl}")]
    private partial void LogTarballDownloadFailed(string tarballUrl, int statusCode);
}
