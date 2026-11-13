using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// Reads gems from the mirrors of a RubyGems feed, which are other gem servers with a compact
/// index, such as <c>https://rubygems.org</c>. The first mirror that lists a gem lists its versions
/// and serves its files.
/// </summary>
public partial class RubyGemsUpstreamClient
{
    /// <summary>
    /// The largest <c>/versions</c> read from a mirror. That of rubygems.org has tens of MiB.
    /// </summary>
    private const long MaxVersionsBytes = 512L * 1024 * 1024;

    // The most read from a mirror's info file or gemspec.
    private const long MaxFileBytes = 16L * 1024 * 1024;

    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IUpstreamClientFactory _clients;
    private readonly UpstreamListingCache _cache;
    private readonly ILogger<RubyGemsUpstreamClient> _logger;

    public RubyGemsUpstreamClient(
        IFeedSettingsResolver feedSettings,
        IUpstreamClientFactory clients,
        UpstreamListingCache cache,
        ILogger<RubyGemsUpstreamClient> logger)
    {
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lists the gem files of a gem from its info file on the first mirror that has it. The listing
    /// is cached like the listings of NuGet mirrors, for the feed's upstream listing cache duration.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The gem name, in the case the mirror knows it by.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The gem files, or an empty list if the feed has no mirrors or none has the gem.</returns>
    public virtual async Task<IReadOnlyList<RubyGemsUpstreamVersion>> ListVersionsAsync(Feed feed, string name, CancellationToken cancellationToken)
    {
        var mirrors = _feedSettings.GetMirrorOptions(feed);
        if (mirrors.Count == 0 || !RubyGemsPackageName.IsValid(name))
        {
            return [];
        }

        return await CachedAsync(
            feed,
            ("gem-info", feed.Id, feed.UpdatedAtUtc.Ticks, name),
            token => ListFromMirrorsAsync(mirrors, feed.Id, name, token),
            cancellationToken);
    }

    /// <summary>
    /// Reads the <c>/versions</c> of each mirror, without its header. Bundler only looks up the
    /// gems listed there, so a feed's <c>/versions</c> includes those of its mirrors.
    /// </summary>
    /// <returns>The lines of each mirror that answered, as UTF-8. Empty if the feed has no mirrors.</returns>
    public virtual async Task<IReadOnlyList<byte[]>> GetVersionsFilesAsync(Feed feed, CancellationToken cancellationToken)
    {
        var mirrors = _feedSettings.GetMirrorOptions(feed);
        if (mirrors.Count == 0)
        {
            return [];
        }

        // One entry per mirror, so the cache counts this large listing as small. It is kept once
        // per feed, and only for the listing cache duration.
        return await CachedAsync<byte[]>(
            feed,
            ("gem-versions", feed.Id, feed.UpdatedAtUtc.Ticks),
            async token =>
            {
                var files = new List<byte[]>();
                foreach (var mirror in mirrors)
                {
                    var file = await TryMirrorAsync(mirror, feed.Id, "versions", (response, _, t) => ReadVersionsBodyAsync(response, t), token);
                    if (file != null)
                    {
                        files.Add(file);
                    }
                }

                return files;
            },
            cancellationToken);
    }

    /// <summary>
    /// Downloads a gem file from the mirror that lists it, to a temporary file.
    /// </summary>
    /// <returns>The gem, or null if the mirror does not have it.</returns>
    public virtual async Task<ArtifactFileContent> DownloadGemOrNullAsync(Feed feed, RubyGemsUpstreamVersion version, long maxBytes, CancellationToken cancellationToken)
    {
        return await TryMirrorAsync(
            version.Mirror,
            feed.Id,
            $"gems/{Uri.EscapeDataString(version.FullName)}{RubyGemsFileName.GemExtension}",
            async (response, _, token) =>
            {
                await using var content = await response.Content.ReadAsStreamAsync(token);
                return await ArtifactFileContent.ReadAsync(content, maxBytes, token);
            },
            cancellationToken);
    }

    /// <summary>
    /// Downloads the <c>quick/Marshal.4.8</c> gemspec of a gem file from the mirror that lists it.
    /// </summary>
    /// <returns>The compressed gemspec, or null if the mirror does not have it.</returns>
    public virtual async Task<byte[]> GetQuickSpecOrNullAsync(Feed feed, RubyGemsUpstreamVersion version, CancellationToken cancellationToken)
    {
        return await TryMirrorAsync(
            version.Mirror,
            feed.Id,
            $"quick/Marshal.4.8/{Uri.EscapeDataString(version.FullName)}{RubyGemsFileName.QuickSpecExtension}",
            (response, _, token) => ReadBytesAsync(response, MaxFileBytes, token),
            cancellationToken);
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

    private async Task<IReadOnlyList<RubyGemsUpstreamVersion>> ListFromMirrorsAsync(
        IReadOnlyList<MirrorOptions> mirrors,
        Guid feedId,
        string name,
        CancellationToken cancellationToken)
    {
        foreach (var mirror in mirrors)
        {
            var versions = await TryMirrorAsync(
                mirror,
                feedId,
                $"info/{Uri.EscapeDataString(name)}",
                async (response, m, token) =>
                {
                    var text = Encoding.UTF8.GetString(await ReadBytesAsync(response, MaxFileBytes, token));
                    return ParseInfo(text, name, m);
                },
                cancellationToken);

            if (versions?.Count > 0)
            {
                return versions;
            }
        }

        return [];
    }

    private static List<RubyGemsUpstreamVersion> ParseInfo(string text, string name, MirrorOptions mirror)
    {
        var result = new List<RubyGemsUpstreamVersion>();
        var inHeader = text.Contains($"\n{RubyGemsCompactIndex.HeaderEnd}\n", StringComparison.Ordinal)
            || text.StartsWith($"{RubyGemsCompactIndex.HeaderEnd}\n", StringComparison.Ordinal);

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (inHeader)
            {
                inHeader = line != RubyGemsCompactIndex.HeaderEnd;
                continue;
            }

            // Lines this server could not serve are left out.
            if (RubyGemsCompactIndex.TryParseVersionEntry(line, out var version, out var platform))
            {
                result.Add(new RubyGemsUpstreamVersion
                {
                    Name = name,
                    Version = version,
                    Platform = platform,
                    Line = line,
                    Sha256 = RubyGemsCompactIndex.GetChecksum(line),
                    Mirror = mirror,
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Reads a <c>/versions</c>, dropping its header: the lines up to <c>---</c>.
    /// </summary>
    private static async Task<byte[]> ReadVersionsBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await ReadBytesAsync(response, MaxVersionsBytes, cancellationToken);

        var header = "---\n"u8;
        var start = content.AsSpan().StartsWith(header) ? header.Length : -1;
        if (start < 0)
        {
            var index = content.AsSpan().IndexOf("\n---\n"u8);
            start = index < 0 ? 0 : index + 5;
        }

        return content[start..];
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
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or ArtifactFileTooLargeException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A mirror that is down or answers with something else is skipped, like a NuGet mirror.
            LogRequestFailed(e, url);
            return default;
        }
    }

    private static async Task<byte[]> ReadBytesAsync(HttpResponseMessage response, long maxBytes, CancellationToken cancellationToken)
    {
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var limited = new LimitedReadStream(
            content,
            maxBytes,
            () => new InvalidDataException($"The response is larger than {maxBytes / 1024 / 1024} MiB."));

        using var buffer = new MemoryStream();
        await limited.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
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
