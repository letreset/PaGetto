using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Cran;

/// <summary>
/// Reads packages from the mirrors of a CRAN feed, which are CRAN-like repositories such as
/// <c>https://cloud.r-project.org</c>. A mirror's current versions are in its
/// <c>src/contrib/PACKAGES.gz</c> index, its older versions in <c>src/contrib/Archive/{name}/</c>,
/// whose directory listing is read. The mirrors are tried in order: the first one whose index has
/// a package serves it.
/// See: https://cran.r-project.org/doc/manuals/r-release/R-admin.html#Setting-up-a-package-repository
/// </summary>
public partial class CranUpstreamClient
{
    private const string Contrib = "src/contrib/";

    // The largest index read, once decompressed. CRAN's is about 15 MiB.
    private const long MaxIndexBytes = 256L * 1024 * 1024;
    private const long MaxListingBytes = 16L * 1024 * 1024;

    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IUpstreamClientFactory _clients;
    private readonly UpstreamListingCache _cache;
    private readonly ILogger<CranUpstreamClient> _logger;

    public CranUpstreamClient(
        IFeedSettingsResolver feedSettings,
        IUpstreamClientFactory clients,
        UpstreamListingCache cache,
        ILogger<CranUpstreamClient> logger)
    {
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The current version of every package on the feed's mirrors, by lowercased name. A package
    /// on several mirrors is the first mirror's. Each mirror's index is cached for the feed's
    /// upstream listing cache duration.
    /// </summary>
    public virtual async Task<IReadOnlyDictionary<string, CranUpstreamVersion>> GetIndexAsync(Feed feed, CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, CranUpstreamVersion>(StringComparer.Ordinal);
        foreach (var index in await GetMirrorIndexesAsync(feed, cancellationToken))
        {
            foreach (var (name, version) in index)
            {
                merged.TryAdd(name, version);
            }
        }

        return merged;
    }

    /// <summary>
    /// Lists the versions of a package on the feed's mirrors: its current version from the index
    /// of the first mirror that has the package, and its older versions from that mirror's archive.
    /// When no mirror's index has the package (it was removed from CRAN), the first mirror whose
    /// archive has it lists them.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The package name as the client wrote it.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The versions, or an empty list if no mirror has the package.</returns>
    public virtual async Task<IReadOnlyList<CranUpstreamVersion>> ListVersionsAsync(Feed feed, string name, CancellationToken cancellationToken)
    {
        var mirrors = _feedSettings.GetMirrorOptions(feed);
        if (mirrors.Count == 0)
        {
            return [];
        }

        var key = CranPackageName.Normalize(name);
        var indexes = await GetMirrorIndexesAsync(feed, cancellationToken);
        var current = indexes.Select(i => i.GetValueOrDefault(key)).FirstOrDefault(v => v != null);

        // The archive's folder is named as the package is published.
        var publishedName = current?.Name ?? name;
        IReadOnlyList<MirrorOptions> candidates = current == null ? mirrors : [current.Mirror];

        var archived = await CachedAsync<CranUpstreamVersion>(
            feed,
            ("cran-archive", feed.Id, feed.UpdatedAtUtc.Ticks, key),
            async token =>
            {
                foreach (var mirror in candidates)
                {
                    var versions = await TryListArchiveAsync(mirror, feed.Id, publishedName, token);
                    if (versions?.Count > 0)
                    {
                        return versions;
                    }
                }

                return [];
            },
            cancellationToken);

        var all = archived.Where(v => current == null || v.Version.Normalized != current.Version.Normalized).ToList();
        if (current != null)
        {
            all.Add(current);
        }

        return all;
    }

    /// <summary>
    /// Downloads a version's source package to a temporary file. A current version is checked
    /// against the MD5 of the mirror's index.
    /// </summary>
    /// <returns>The package, or null if it can't be downloaded or doesn't match its MD5.</returns>
    public virtual async Task<ArtifactFileContent> DownloadOrNullAsync(Feed feed, CranUpstreamVersion version, long maxBytes, CancellationToken cancellationToken)
    {
        var url = GetContribUrl(version.Mirror) + version.Path;

        try
        {
            using var client = _clients.CreateHttpClient(version.Mirror, feed.Id, authenticate: true);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogUpstreamFailed(url, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var content = await ArtifactFileContent.ReadAsync(stream, maxBytes, cancellationToken);

            var md5 = version.Md5;
            if (!string.IsNullOrEmpty(md5) && !string.Equals(md5, content.Md5, StringComparison.OrdinalIgnoreCase))
            {
                content.Dispose();
                LogChecksumMismatch(url);
                return null;
            }

            return content;
        }
        catch (Exception e) when (e is HttpRequestException or ArtifactFileTooLargeException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogRequestFailed(e, url);
            return null;
        }
    }

    /// <summary>
    /// The index of each mirror that answered, in the order of the mirrors.
    /// </summary>
    private async Task<List<IReadOnlyDictionary<string, CranUpstreamVersion>>> GetMirrorIndexesAsync(Feed feed, CancellationToken cancellationToken)
    {
        var indexes = new List<IReadOnlyDictionary<string, CranUpstreamVersion>>();
        foreach (var mirror in _feedSettings.GetMirrorOptions(feed))
        {
            // One entry per mirror, so the cache counts this large listing as small. It is kept
            // once per feed, and only for the listing cache duration.
            var index = await CachedAsync<IReadOnlyDictionary<string, CranUpstreamVersion>>(
                feed,
                ("cran-index", feed.Id, feed.UpdatedAtUtc.Ticks, mirror.PackageSource.AbsoluteUri),
                async token => await TryReadIndexAsync(mirror, feed.Id, token) is { } read ? [read] : [],
                cancellationToken);

            if (index.Count > 0)
            {
                indexes.Add(index[0]);
            }
        }

        return indexes;
    }

    /// <summary>
    /// Reads a mirror's <c>PACKAGES.gz</c>.
    /// </summary>
    /// <returns>The newest version of each package, or null if the mirror is down or answers with something else.</returns>
    private async Task<Dictionary<string, CranUpstreamVersion>> TryReadIndexAsync(MirrorOptions mirror, Guid feedId, CancellationToken cancellationToken)
    {
        var url = GetContribUrl(mirror) + "PACKAGES.gz";

        try
        {
            using var client = _clients.CreateHttpClient(mirror, feedId, authenticate: true);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogUpstreamFailed(url, (int)response.StatusCode);
                return null;
            }

            // The compressed index is read first, since HttpClient may have decompressed it already
            // (when the server sends it with a gzip content encoding).
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var body = new MemoryStream();
            await using (var limitedBody = new LimitedReadStream(content, MaxIndexBytes, IndexTooLarge))
            {
                await limitedBody.CopyToAsync(body, cancellationToken);
            }

            body.Position = 0;
            var isGzip = body.Length >= 2 && body.GetBuffer()[0] == 0x1f && body.GetBuffer()[1] == 0x8b;
            await using var text = isGzip ? new GZipStream(body, CompressionMode.Decompress) : (Stream)body;
            await using var limited = new LimitedReadStream(text, MaxIndexBytes, IndexTooLarge);
            using var reader = new StreamReader(limited, Encoding.UTF8);

            var index = new Dictionary<string, CranUpstreamVersion>(StringComparer.Ordinal);
            foreach (var entry in CranDcf.ReadRecords(reader))
            {
                if (ToUpstreamVersion(entry, mirror) is not { } version)
                {
                    continue;
                }

                // An index may list a package more than once, e.g. CRAN's recommended packages for
                // each R version; the newest is kept, and of the same version the one at the top.
                var key = CranPackageName.Normalize(version.Name);
                if (!index.TryGetValue(key, out var existing)
                    || version.Version.CompareTo(existing.Version) > 0
                    || (version.Version.CompareTo(existing.Version) == 0 && !version.Path.Contains('/') && existing.Path.Contains('/')))
                {
                    index[key] = version;
                }
            }

            return index;
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A mirror that is down or answers with something else is skipped, like a NuGet mirror.
            LogRequestFailed(e, url);
            return null;
        }
    }

    private static CranUpstreamVersion ToUpstreamVersion(IReadOnlyList<KeyValuePair<string, string>> entry, MirrorOptions mirror)
    {
        var name = CranDcf.GetValue(entry, CranManifest.PackageField);
        if (!CranPackageName.IsValid(name) || !CranVersion.TryParse(CranDcf.GetValue(entry, CranManifest.VersionField), out var version))
        {
            return null;
        }

        // An entry with a Path is in that folder below src/contrib, and one with a File has that file name.
        var file = CranDcf.GetValue(entry, "File") ?? CranFileName.Create(name, version.Text);
        var folder = CranDcf.GetValue(entry, "Path")?.Trim('/');
        var path = string.IsNullOrEmpty(folder) ? file : $"{folder}/{file}";
        if (path.Contains("..", StringComparison.Ordinal) || path.Contains('\\') || path.Contains(':'))
        {
            return null;
        }

        return new CranUpstreamVersion
        {
            Name = name,
            Version = version,
            Path = path,
            IndexEntry = entry,
            Mirror = mirror,
        };
    }

    /// <summary>
    /// Reads the directory listing of a package's folder in a mirror's archive.
    /// </summary>
    /// <returns>The versions, or null if the mirror has no such folder, is down, or answers with something else.</returns>
    private async Task<List<CranUpstreamVersion>> TryListArchiveAsync(MirrorOptions mirror, Guid feedId, string name, CancellationToken cancellationToken)
    {
        var folder = $"Archive/{Uri.EscapeDataString(name)}/";
        var url = GetContribUrl(mirror) + folder;

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
                MaxListingBytes,
                () => new InvalidDataException($"The listing is larger than {MaxListingBytes / 1024 / 1024} MiB."));
            using var reader = new StreamReader(limited, Encoding.UTF8);
            var listing = await reader.ReadToEndAsync(cancellationToken);

            var versions = new Dictionary<string, CranUpstreamVersion>(StringComparer.Ordinal);
            foreach (Match link in LinkRegex().Matches(listing))
            {
                var file = WebUtility.HtmlDecode(Uri.UnescapeDataString(link.Groups[1].Value));
                if (CranFileName.TryParse(file, out var fileName, out var version) && fileName == name)
                {
                    versions.TryAdd(version.Normalized, new CranUpstreamVersion
                    {
                        Name = name,
                        Version = version,
                        Path = folder + CranFileName.Create(name, version.Text),
                        Mirror = mirror,
                    });
                }
            }

            return versions.Values.ToList();
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogRequestFailed(e, url);
            return null;
        }
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

    private static string GetContribUrl(MirrorOptions mirror)
    {
        var baseUrl = mirror.PackageSource.AbsoluteUri;
        return (baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/") + Contrib;
    }

    private static InvalidDataException IndexTooLarge()
    {
        return new InvalidDataException($"The index is larger than {MaxIndexBytes / 1024 / 1024} MiB.");
    }

    // The links of a directory listing, such as Apache's, to source packages.
    [GeneratedRegex(@"href=""(?:[^""]*/)?([^""/]+\.tar\.gz)""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream answered {StatusCode} for {UpstreamUrl}")]
    private partial void LogUpstreamFailed(string upstreamUrl, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read {UpstreamUrl}")]
    private partial void LogRequestFailed(Exception exception, string upstreamUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The package at {UpstreamUrl} does not match the MD5 of the mirror's index")]
    private partial void LogChecksumMismatch(string upstreamUrl);
}
