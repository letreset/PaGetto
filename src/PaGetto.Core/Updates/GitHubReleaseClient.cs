using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace PaGetto.Core.Updates;

/// <summary>
/// Reads the latest PaGetto release from the GitHub API. The request carries nothing about the
/// server: no version, no credentials, no cookies.
/// </summary>
public class GitHubReleaseClient
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/letreset/PaGetto/releases/latest";
    public const string ReleasesUrl = "https://github.com/letreset/PaGetto/releases/tag/";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    // Pooled connections and no cookies, like the upstream requests.
    private static readonly SocketsHttpHandler _sharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip,
    };

    private readonly HttpMessageHandler _handler;

    /// <param name="handlerOverride">The handler requests are sent with; tests send them to a fake.</param>
    public GitHubReleaseClient(HttpMessageHandler handlerOverride = null)
    {
        _handler = handlerOverride ?? _sharedHandler;
    }

    /// <summary>
    /// Reads the latest release, which GitHub picks among the releases that are not prereleases.
    /// </summary>
    /// <returns>The release, or null if its tag is not a version.</returns>
    /// <exception cref="HttpRequestException">GitHub could not be reached or answered with an error.</exception>
    public virtual async Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient(_handler, disposeHandler: false) { Timeout = Timeout };
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);

        // GitHub refuses requests without a user agent.
        request.Headers.UserAgent.ParseAdd("PaGetto");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("tag_name", out var tagElement)
            || tagElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        // Release tags are "v" and the version, e.g. v1.2.0.
        var tag = tagElement.GetString();
        var version = tag.StartsWith('v') ? tag[1..] : tag;
        if (!NuGetVersion.TryParse(version, out var parsed))
        {
            return null;
        }

        // The link is built here rather than taken from the answer, so it always points to GitHub.
        return new ReleaseInfo(parsed, new Uri(ReleasesUrl + Uri.EscapeDataString(tag)));
    }
}
