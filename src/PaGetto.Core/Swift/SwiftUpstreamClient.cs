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

namespace PaGetto.Core.Swift;

/// <summary>
/// Reads packages from the mirrors of a Swift feed, which are Swift package registries. The mirrors
/// are tried in order: the first one that has a package lists its releases, and a release's
/// metadata and source archive come from the mirror that listed it.
/// See: https://github.com/swiftlang/swift-package-manager/blob/main/Documentation/PackageRegistry/Registry.md
/// </summary>
public partial class SwiftUpstreamClient
{
    /// <summary>
    /// The media type of the JSON answers of version 1 of the registry API.
    /// </summary>
    public const string JsonMediaType = "application/vnd.swift.registry.v1+json";

    /// <summary>
    /// The media type of the source archives of version 1 of the registry API.
    /// </summary>
    public const string ZipMediaType = "application/vnd.swift.registry.v1+zip";

    // The most read for one release list, release metadata or identifier lookup.
    private const long MaxJsonBytes = 16L * 1024 * 1024;

    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IUpstreamClientFactory _clients;
    private readonly UpstreamListingCache _cache;
    private readonly ILogger<SwiftUpstreamClient> _logger;

    public SwiftUpstreamClient(
        IFeedSettingsResolver feedSettings,
        IUpstreamClientFactory clients,
        UpstreamListingCache cache,
        ILogger<SwiftUpstreamClient> logger)
    {
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lists the releases of a package on the feed's mirrors, leaving out those the mirror marks as
    /// unavailable. The listing is cached like the listings of NuGet mirrors, for the feed's upstream
    /// listing cache duration.
    /// </summary>
    /// <returns>The releases, or an empty list if the feed has no mirrors or none has the package.</returns>
    public virtual async Task<IReadOnlyList<SwiftUpstreamRelease>> ListReleasesAsync(Feed feed, string scope, string name, CancellationToken cancellationToken)
    {
        var mirrors = _feedSettings.GetMirrorOptions(feed);
        if (mirrors.Count == 0)
        {
            return [];
        }

        var cacheDuration = _feedSettings.GetUpstreamListingCacheDuration(feed);
        if (cacheDuration <= TimeSpan.Zero)
        {
            return await ListFromMirrorsAsync(mirrors, feed.Id, scope, name, cancellationToken);
        }

        // Saving a feed (including its mirrors) updates UpdatedAtUtc, which invalidates its entries.
        var key = ("swift-releases", feed.Id, feed.UpdatedAtUtc.Ticks, SwiftPackageIdentity.Normalize(scope, name));

        return await _cache.GetOrListAsync(
            key,
            cacheDuration,
            token => ListFromMirrorsAsync(mirrors, feed.Id, scope, name, token),
            cancellationToken);
    }

    /// <summary>
    /// Reads the metadata of a release from the mirror that lists it.
    /// </summary>
    /// <returns>The metadata, or null if the mirror does not have it or it can't be read.</returns>
    public virtual async Task<JsonObject> GetReleaseOrNullAsync(Feed feed, SwiftUpstreamRelease release, string scope, string name, CancellationToken cancellationToken)
    {
        var url = GetUrl(release.Mirror, scope, name, release.Version);

        return await TryReadJsonAsync(release.Mirror, feed.Id, url, cancellationToken) as JsonObject;
    }

    /// <summary>
    /// Downloads the source archive of a release from the mirror that lists it, to a temporary file.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="release">The release, as listed by <see cref="ListReleasesAsync"/>.</param>
    /// <param name="scope">The package's scope.</param>
    /// <param name="name">The package's name.</param>
    /// <param name="maxBytes">The largest archive accepted.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The archive, or null if it can't be downloaded.</returns>
    public virtual async Task<ArtifactFileContent> DownloadArchiveOrNullAsync(
        Feed feed,
        SwiftUpstreamRelease release,
        string scope,
        string name,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var url = GetUrl(release.Mirror, scope, name, release.Version) + ".zip";

        try
        {
            using var client = _clients.CreateHttpClient(release.Mirror, feed.Id, authenticate: true);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd(ZipMediaType);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
    /// Looks up the identifiers of the packages of a repository URL on the feed's mirrors. The first
    /// mirror that knows the URL answers.
    /// </summary>
    /// <returns>The identifiers, or an empty list.</returns>
    public virtual async Task<IReadOnlyList<string>> LookupIdentifiersAsync(Feed feed, string repositoryUrl, CancellationToken cancellationToken)
    {
        foreach (var mirror in _feedSettings.GetMirrorOptions(feed))
        {
            var url = $"{GetBaseUrl(mirror)}/identifiers?url={Uri.EscapeDataString(repositoryUrl)}";
            if (await TryReadJsonAsync(mirror, feed.Id, url, cancellationToken) is JsonObject answer
                && answer["identifiers"] is JsonArray identifiers)
            {
                var valid = identifiers
                    .Select(i => i is JsonValue value && value.TryGetValue<string>(out var text) ? text : null)
                    .Where(i => SwiftPackageIdentity.TryParse(i, out _, out _))
                    .ToList();

                if (valid.Count > 0)
                {
                    return valid;
                }
            }
        }

        return [];
    }

    private async Task<IReadOnlyList<SwiftUpstreamRelease>> ListFromMirrorsAsync(
        IReadOnlyList<MirrorOptions> mirrors,
        Guid feedId,
        string scope,
        string name,
        CancellationToken cancellationToken)
    {
        foreach (var mirror in mirrors)
        {
            if (await TryReadJsonAsync(mirror, feedId, GetUrl(mirror, scope, name, version: null), cancellationToken) is not JsonObject list
                || list["releases"] is not JsonObject releases)
            {
                continue;
            }

            var available = new List<SwiftUpstreamRelease>();
            foreach (var (version, release) in releases)
            {
                // A release with a problem, such as one the registry removed, can't be downloaded.
                if (SwiftVersion.TryParse(version, out _) && release is JsonObject item && item["problem"] == null)
                {
                    available.Add(new SwiftUpstreamRelease { Version = version, Mirror = mirror });
                }
            }

            if (available.Count > 0)
            {
                return available;
            }
        }

        return [];
    }

    /// <summary>
    /// Reads a JSON answer of a mirror.
    /// </summary>
    /// <returns>The JSON, or null if the mirror does not have it, is down, or answers with something else.</returns>
    private async Task<JsonNode> TryReadJsonAsync(MirrorOptions mirror, Guid feedId, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var client = _clients.CreateHttpClient(mirror, feedId, authenticate: true);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd(JsonMediaType);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
                MaxJsonBytes,
                () => new InvalidDataException($"The answer is larger than {MaxJsonBytes / 1024 / 1024} MiB."));

            return await JsonNode.ParseAsync(limited, cancellationToken: cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidDataException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            UpstreamListingCache.ReportFailure();
            // A mirror that is down or answers with something else is skipped, like a NuGet mirror.
            LogRequestFailed(e, url);
            return null;
        }
    }

    private static string GetUrl(MirrorOptions mirror, string scope, string name, string version)
    {
        var url = $"{GetBaseUrl(mirror)}/{Uri.EscapeDataString(scope)}/{Uri.EscapeDataString(name)}";
        return version == null ? url : $"{url}/{Uri.EscapeDataString(version)}";
    }

    private static string GetBaseUrl(MirrorOptions mirror)
    {
        return mirror.PackageSource.AbsoluteUri.TrimEnd('/');
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream answered {StatusCode} for {UpstreamUrl}")]
    private partial void LogUpstreamFailed(string upstreamUrl, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read {UpstreamUrl}")]
    private partial void LogRequestFailed(Exception exception, string upstreamUrl);
}
