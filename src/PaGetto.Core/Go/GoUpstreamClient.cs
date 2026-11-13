using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Go;

/// <summary>
/// Reads modules from the mirrors of a Go feed, which are other module proxies such as
/// <c>https://proxy.golang.org</c>. The mirrors are tried in order: the first one that lists a
/// module lists its versions, and the first one that has a version serves it.
/// See: https://go.dev/ref/mod#goproxy-protocol
/// </summary>
public partial class GoUpstreamClient
{
    // The most text read from a mirror's list, .info or .mod response.
    private const long MaxTextBytes = GoModuleZipReader.MaxGoModBytes;

    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IUpstreamClientFactory _clients;
    private readonly UpstreamListingCache _cache;
    private readonly ILogger<GoUpstreamClient> _logger;

    public GoUpstreamClient(
        IFeedSettingsResolver feedSettings,
        IUpstreamClientFactory clients,
        UpstreamListingCache cache,
        ILogger<GoUpstreamClient> logger)
    {
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lists the versions of a module on the feed's mirrors (its <c>@v/list</c>, without
    /// pseudo-versions). The listing is cached like the listings of NuGet mirrors, for the feed's
    /// upstream listing cache duration.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="escapedPath">The module's escaped path.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The versions, or an empty list if the feed has no mirrors or none lists a version of the module.</returns>
    public virtual async Task<IReadOnlyList<string>> ListVersionsAsync(Feed feed, string escapedPath, CancellationToken cancellationToken)
    {
        var mirrors = _feedSettings.GetMirrorOptions(feed);
        if (mirrors.Count == 0)
        {
            return [];
        }

        var cacheDuration = _feedSettings.GetUpstreamListingCacheDuration(feed);
        if (cacheDuration <= TimeSpan.Zero)
        {
            return await ListFromMirrorsAsync(mirrors, feed.Id, escapedPath, cancellationToken);
        }

        // Saving a feed (including its mirrors) updates UpdatedAtUtc, which invalidates its entries.
        var key = ("go-versions", feed.Id, feed.UpdatedAtUtc.Ticks, escapedPath);

        return await _cache.GetOrListAsync(
            key,
            cacheDuration,
            token => ListFromMirrorsAsync(mirrors, feed.Id, escapedPath, token),
            cancellationToken);
    }

    /// <summary>
    /// Gets the <c>.info</c> of a version, or of another query the mirror resolves, such as a branch name.
    /// </summary>
    /// <returns>The info, or null if no mirror has it.</returns>
    public virtual Task<GoUpstreamInfo> GetInfoOrNullAsync(Feed feed, string escapedPath, string escapedQuery, CancellationToken cancellationToken)
    {
        return GetInfoOrNullAsync(feed, $"{escapedPath}/@v/{escapedQuery}.info", cancellationToken);
    }

    /// <summary>
    /// Gets the <c>@latest</c> of a module: the info of the version the mirror considers latest,
    /// which may be a pseudo-version when the module has no tagged version.
    /// </summary>
    /// <returns>The info, or null if no mirror has the module.</returns>
    public virtual Task<GoUpstreamInfo> GetLatestOrNullAsync(Feed feed, string escapedPath, CancellationToken cancellationToken)
    {
        return GetInfoOrNullAsync(feed, $"{escapedPath}/@latest", cancellationToken);
    }

    /// <summary>
    /// Gets the <c>go.mod</c> file of a version.
    /// </summary>
    /// <returns>The file's content, or null if no mirror has the version.</returns>
    public virtual async Task<string> GetModOrNullAsync(Feed feed, string escapedPath, string escapedVersion, CancellationToken cancellationToken)
    {
        return await FirstFromMirrorsAsync(
            feed,
            $"{escapedPath}/@v/{escapedVersion}.mod",
            (response, _, token) => ReadTextAsync(response, token),
            cancellationToken);
    }

    /// <summary>
    /// Downloads the zip of a version from the first mirror that has it, to a temporary file.
    /// </summary>
    /// <returns>The zip and the mirror it came from, or null if no mirror has the version.</returns>
    public virtual async Task<(ArtifactFileContent Zip, MirrorOptions Mirror)?> DownloadZipOrNullAsync(
        Feed feed,
        string escapedPath,
        string escapedVersion,
        CancellationToken cancellationToken)
    {
        return await FirstFromMirrorsAsync<(ArtifactFileContent, MirrorOptions)?>(
            feed,
            $"{escapedPath}/@v/{escapedVersion}.zip",
            async (response, mirror, token) =>
            {
                await using var content = await response.Content.ReadAsStreamAsync(token);
                return (await ArtifactFileContent.ReadAsync(content, GoModuleZipReader.MaxUncompressedBytes, token), mirror);
            },
            cancellationToken);
    }

    private async Task<GoUpstreamInfo> GetInfoOrNullAsync(Feed feed, string relativeUrl, CancellationToken cancellationToken)
    {
        return await FirstFromMirrorsAsync(
            feed,
            relativeUrl,
            async (response, mirror, token) =>
            {
                using var info = JsonDocument.Parse(await ReadTextAsync(response, token));
                var root = info.RootElement;

                var version = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Version", out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString()
                    : null;

                if (!GoVersion.TryParse(version, out _))
                {
                    throw new InvalidDataException($"The info has no canonical version: {version}.");
                }

                DateTime? time = root.TryGetProperty("Time", out var t) && t.ValueKind == JsonValueKind.String && t.TryGetDateTime(out var parsed)
                    ? parsed.ToUniversalTime()
                    : null;

                return new GoUpstreamInfo { Version = version, Time = time, Mirror = mirror };
            },
            cancellationToken);
    }

    private async Task<IReadOnlyList<string>> ListFromMirrorsAsync(
        IReadOnlyList<MirrorOptions> mirrors,
        Guid feedId,
        string escapedPath,
        CancellationToken cancellationToken)
    {
        foreach (var mirror in mirrors)
        {
            var versions = await TryMirrorAsync(
                mirror,
                feedId,
                $"{escapedPath}/@v/list",
                async (response, _, token) =>
                {
                    var list = new List<string>();
                    foreach (var line in (await ReadTextAsync(response, token)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        // Versions this server could not serve are left out.
                        if (GoVersion.TryParse(line, out var version) && !version.IsPseudo)
                        {
                            list.Add(line);
                        }
                    }

                    return list;
                },
                cancellationToken);

            if (versions?.Count > 0)
            {
                return versions;
            }
        }

        return [];
    }

    private async Task<T> FirstFromMirrorsAsync<T>(
        Feed feed,
        string relativeUrl,
        Func<HttpResponseMessage, MirrorOptions, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        foreach (var mirror in _feedSettings.GetMirrorOptions(feed))
        {
            var result = await TryMirrorAsync(mirror, feed.Id, relativeUrl, read, cancellationToken);
            if (result != null)
            {
                return result;
            }
        }

        return default;
    }

    /// <summary>
    /// Requests a URL of a mirror and reads the response.
    /// </summary>
    /// <returns>What was read, or null if the mirror does not have it, is down, or answers with something else.</returns>
    private async Task<T> TryMirrorAsync<T>(
        MirrorOptions mirror,
        Guid feedId,
        string relativeUrl,
        Func<HttpResponseMessage, MirrorOptions, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        var url = GetBaseUrl(mirror) + relativeUrl;

        try
        {
            using var client = _clients.CreateHttpClient(mirror, feedId, authenticate: true);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            // A module proxy answers 404 or 410 for what it does not have.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                LogUpstreamFailed(url, (int)response.StatusCode);
                return default;
            }

            return await read(response, mirror, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidDataException or ArtifactFileTooLargeException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A mirror that is down or answers with something else is skipped, like a NuGet mirror.
            LogRequestFailed(e, url);
            return default;
        }
    }

    private static async Task<string> ReadTextAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var limited = new LimitedReadStream(
            content,
            MaxTextBytes,
            () => new InvalidDataException($"The response is larger than {MaxTextBytes / 1024 / 1024} MiB."));

        // The text is kept as it is, so a .mod file keeps its bytes.
        using var reader = new StreamReader(limited, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// The mirror's URL with a trailing slash, so it can be used as a prefix.
    /// </summary>
    private static string GetBaseUrl(MirrorOptions mirror)
    {
        var url = mirror.PackageSource.AbsoluteUri;
        return url.EndsWith('/') ? url : url + "/";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream answered {StatusCode} for {UpstreamUrl}")]
    private partial void LogUpstreamFailed(string upstreamUrl, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read {UpstreamUrl}")]
    private partial void LogRequestFailed(Exception exception, string upstreamUrl);
}
