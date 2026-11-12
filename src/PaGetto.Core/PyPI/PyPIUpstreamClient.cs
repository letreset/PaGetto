using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.PyPI;

/// <summary>
/// Reads from the mirrors of a PyPI feed, which are other simple repositories such as
/// <c>https://pypi.org/simple</c>. The first mirror that has a project serves all of its files.
/// Mirrors are asked for the JSON form of the simple API (PEP 691) and may answer with the HTML form
/// (PEP 503).
/// </summary>
public partial class PyPIUpstreamClient
{
    /// <summary>
    /// The <c>Accept</c> header of requests for a project's page.
    /// </summary>
    public const string AcceptHeader = "application/vnd.pypi.simple.v1+json, application/vnd.pypi.simple.v1+html;q=0.2, text/html;q=0.01";

    // Pages of projects with thousands of files are a few MiB; this bounds what a mirror can make
    // the server read.
    private const long MaxPageBytes = 64 * 1024 * 1024;

    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IUpstreamClientFactory _clients;
    private readonly UpstreamListingCache _cache;
    private readonly ILogger<PyPIUpstreamClient> _logger;

    public PyPIUpstreamClient(
        IFeedSettingsResolver feedSettings,
        IUpstreamClientFactory clients,
        UpstreamListingCache cache,
        ILogger<PyPIUpstreamClient> logger)
    {
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lists the wheels and sdists of a project from the first of the feed's mirrors that has it.
    /// The listing is cached like the listings of NuGet mirrors, for the feed's upstream listing
    /// cache duration.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The normalized project name.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The files. Empty if the feed has no mirrors or none has the project.</returns>
    public virtual async Task<IReadOnlyList<PyPIUpstreamFile>> GetFilesAsync(Feed feed, string name, CancellationToken cancellationToken)
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
        var key = ("pypi-files", feed.Id, feed.UpdatedAtUtc.Ticks, name);

        return await _cache.GetOrListAsync(
            key,
            cacheDuration,
            token => ListFromMirrorsAsync(mirrors, feed.Id, name, token),
            cancellationToken);
    }

    /// <summary>
    /// Downloads a file to a temporary file, and checks it against the SHA-256 the mirror lists.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="file">The file.</param>
    /// <param name="maxBytes">The most the file may have.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The file, or null if the mirror does not serve it.</returns>
    /// <exception cref="InvalidDataException">The file does not match the mirror's SHA-256.</exception>
    /// <exception cref="ArtifactFileTooLargeException">The file is larger than <paramref name="maxBytes"/>.</exception>
    public virtual async Task<ArtifactFileContent> DownloadFileOrNullAsync(Feed feed, PyPIUpstreamFile file, long maxBytes, CancellationToken cancellationToken)
    {
        // Files may be served from another host than the mirror (pypi.org's are on
        // files.pythonhosted.org), which must not get its credentials.
        var isMirrorHost = Uri.Compare(
            new Uri(file.Url),
            file.Mirror.PackageSource,
            UriComponents.SchemeAndServer,
            UriFormat.Unescaped,
            StringComparison.OrdinalIgnoreCase) == 0;

        using var client = _clients.CreateHttpClient(file.Mirror, feed.Id, authenticate: isMirrorHost);
        using var response = await client.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            LogDownloadFailed(file.Url, (int)response.StatusCode);
            return null;
        }

        ArtifactFileContent content;
        await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            content = await ArtifactFileContent.ReadAsync(body, maxBytes, cancellationToken);
        }

        if (file.Sha256 != null && !string.Equals(file.Sha256, content.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            content.Dispose();
            throw new InvalidDataException(
                $"The file {file.Url} has the SHA-256 {content.Sha256}, but the mirror lists {file.Sha256}.");
        }

        return content;
    }

    private async Task<IReadOnlyList<PyPIUpstreamFile>> ListFromMirrorsAsync(
        IReadOnlyList<MirrorOptions> mirrors,
        Guid feedId,
        string name,
        CancellationToken cancellationToken)
    {
        foreach (var mirror in mirrors)
        {
            try
            {
                var files = await ListFromMirrorAsync(mirror, feedId, name, cancellationToken);
                if (files.Count > 0)
                {
                    return files;
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

    private async Task<IReadOnlyList<PyPIUpstreamFile>> ListFromMirrorAsync(
        MirrorOptions mirror,
        Guid feedId,
        string name,
        CancellationToken cancellationToken)
    {
        using var client = _clients.CreateHttpClient(mirror, feedId, authenticate: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{mirror.PackageSource.AbsoluteUri.TrimEnd('/')}/{name}/");
        request.Headers.TryAddWithoutValidation("Accept", AcceptHeader);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();

        // Relative URLs are relative to the page, after any redirects.
        var pageUrl = response.RequestMessage?.RequestUri ?? request.RequestUri;

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var limited = new LimitedReadStream(body, MaxPageBytes, () => new InvalidDataException("The project page is too large."));

        var entries = response.Content.Headers.ContentType?.MediaType?.EndsWith("json", StringComparison.OrdinalIgnoreCase) == true
            ? await ReadJsonAsync(limited, cancellationToken)
            : await ReadHtmlAsync(limited, cancellationToken);

        var files = new List<PyPIUpstreamFile>();
        foreach (var entry in entries)
        {
            // Eggs, installers and files whose version is not a PEP 440 version are left out.
            if (!PyPIFileName.TryParse(entry.FileName, name, out var fileName)
                || entry.Url == null
                || !Uri.TryCreate(pageUrl, entry.Url, out var url)
                || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            files.Add(new PyPIUpstreamFile
            {
                FileName = fileName.FileName,
                Version = fileName.Version,
                Url = new UriBuilder(url) { Fragment = string.Empty }.Uri.AbsoluteUri,
                Sha256 = entry.Sha256?.ToLowerInvariant(),
                RequiresPython = string.IsNullOrWhiteSpace(entry.RequiresPython) ? null : entry.RequiresPython,
                Yanked = entry.Yanked,
                YankedReason = string.IsNullOrWhiteSpace(entry.YankedReason) ? null : entry.YankedReason,
                Size = entry.Size,
                UploadTime = entry.UploadTime,
                Mirror = mirror,
            });
        }

        return files;
    }

    /// <summary>
    /// Reads a project page in the JSON form (PEP 691).
    /// </summary>
    private static async Task<List<PageEntry>> ReadJsonAsync(Stream body, CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

        var entries = new List<PageEntry>();
        if (!document.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            return entries;
        }

        foreach (var file in files.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var yanked = file.TryGetProperty("yanked", out var yankedValue) ? yankedValue : default;

            entries.Add(new PageEntry
            {
                FileName = GetString(file, "filename"),
                Url = GetString(file, "url"),
                Sha256 = file.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Object
                    ? GetString(hashes, "sha256")
                    : null,
                RequiresPython = GetString(file, "requires-python"),
                Yanked = yanked.ValueKind is JsonValueKind.True or JsonValueKind.String,
                YankedReason = yanked.ValueKind == JsonValueKind.String ? yanked.GetString() : null,
                Size = file.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : null,
                UploadTime = DateTime.TryParse(
                    GetString(file, "upload-time"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var uploadTime)
                    ? uploadTime
                    : null,
            });
        }

        return entries;
    }

    /// <summary>
    /// Reads a project page in the HTML form (PEP 503): one anchor per file, whose text is the file
    /// name and whose URL fragment has its hash.
    /// </summary>
    private static async Task<List<PageEntry>> ReadHtmlAsync(Stream body, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(body);
        var html = await reader.ReadToEndAsync(cancellationToken);

        var entries = new List<PageEntry>();
        foreach (Match anchor in AnchorRegex().Matches(html))
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in AttributeRegex().Matches(anchor.Groups["attributes"].Value))
            {
                attributes.TryAdd(attribute.Groups["name"].Value, WebUtility.HtmlDecode(attribute.Groups["value"].Value));
            }

            if (!attributes.TryGetValue("href", out var href))
            {
                continue;
            }

            var hash = href.Contains('#', StringComparison.Ordinal) ? href[(href.IndexOf('#', StringComparison.Ordinal) + 1)..] : null;
            var yanked = attributes.TryGetValue("data-yanked", out var reason);

            entries.Add(new PageEntry
            {
                FileName = WebUtility.HtmlDecode(TagRegex().Replace(anchor.Groups["text"].Value, string.Empty)).Trim(),
                Url = href,
                Sha256 = hash?.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase) == true ? hash["sha256=".Length..] : null,
                RequiresPython = attributes.GetValueOrDefault("data-requires-python"),
                Yanked = yanked,
                YankedReason = reason,
            });
        }

        return entries;
    }

    private static string GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    [GeneratedRegex(@"<a\s(?<attributes>[^>]*)>(?<text>.*?)</a\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(@"(?<name>[a-zA-Z_:][-a-zA-Z0-9_:.]*)(?:\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s""'>]+)))?", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to list the files of {Name} from upstream {PackageSource}")]
    private partial void LogListingFailed(Exception exception, string name, Uri packageSource);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream answered {StatusCode} for the file {Url}")]
    private partial void LogDownloadFailed(string url, int statusCode);

    /// <summary>
    /// A file as a project page lists it, before it is checked.
    /// </summary>
    private sealed class PageEntry
    {
        public string FileName { get; init; }
        public string Url { get; init; }
        public string Sha256 { get; init; }
        public string RequiresPython { get; init; }
        public bool Yanked { get; init; }
        public string YankedReason { get; init; }
        public long? Size { get; init; }
        public DateTime? UploadTime { get; init; }
    }
}
