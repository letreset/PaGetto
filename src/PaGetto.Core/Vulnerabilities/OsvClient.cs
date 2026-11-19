using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PaGetto.Core.Configuration;

namespace PaGetto.Core.Vulnerabilities;

/// <summary>
/// Asks the OSV API (<see cref="VulnerabilityScanningOptions.ApiUrl"/>) which advisories affect
/// package versions, and reads the advisories.
/// </summary>
public class OsvClient
{
    /// <summary>
    /// The most queries one <c>/v1/querybatch</c> request may have.
    /// </summary>
    public const int MaxBatchSize = 1000;

    // Pooled connections and no cookies, like the upstream requests.
    private static readonly SocketsHttpHandler _sharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        UseCookies = false,
    };

    private readonly IOptionsSnapshot<VulnerabilityScanningOptions> _options;
    private readonly HttpMessageHandler _handler;

    /// <param name="options">The API's URL and timeout.</param>
    /// <param name="handlerOverride">The handler requests are sent with; tests send them to a fake.</param>
    public OsvClient(IOptionsSnapshot<VulnerabilityScanningOptions> options, HttpMessageHandler handlerOverride = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _handler = handlerOverride ?? _sharedHandler;
    }

    /// <summary>
    /// Finds the advisories that affect each of the versions.
    /// </summary>
    /// <param name="queries">The versions, at most <see cref="MaxBatchSize"/>.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The ids of the advisories of each version, in the order of the queries.</returns>
    /// <exception cref="HttpRequestException">The API could not be reached or answered with an error.</exception>
    public virtual async Task<IReadOnlyList<IReadOnlyList<string>>> QueryBatchAsync(IReadOnlyList<OsvQuery> queries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queries);
        if (queries.Count > MaxBatchSize)
        {
            throw new ArgumentException($"At most {MaxBatchSize} versions can be looked up at once.", nameof(queries));
        }

        var ids = queries.Select(_ => new List<string>()).ToList();
        var pending = Enumerable.Range(0, queries.Count).Select(i => (Index: i, PageToken: (string)null)).ToList();

        using var client = CreateClient();
        while (pending.Count > 0)
        {
            var body = new JsonObject
            {
                ["queries"] = new JsonArray(pending.Select(p => (JsonNode)CreateQuery(queries[p.Index], p.PageToken)).ToArray()),
            };

            using var response = await client.PostAsJsonAsync("v1/querybatch", body, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var results = json.RootElement.TryGetProperty("results", out var r) && r.ValueKind == JsonValueKind.Array
                ? r.EnumerateArray().ToList()
                : [];
            if (results.Count != pending.Count)
            {
                throw new HttpRequestException($"The OSV API answered {results.Count} results to {pending.Count} queries.");
            }

            var next = new List<(int Index, string PageToken)>();
            for (var i = 0; i < results.Count; i++)
            {
                var result = results[i];
                if (result.TryGetProperty("vulns", out var vulns) && vulns.ValueKind == JsonValueKind.Array)
                {
                    foreach (var vuln in vulns.EnumerateArray())
                    {
                        if (vuln.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                        {
                            ids[pending[i].Index].Add(id.GetString());
                        }
                    }
                }

                // A version with very many advisories gets them in pages.
                if (result.TryGetProperty("next_page_token", out var token)
                    && token.ValueKind == JsonValueKind.String
                    && !string.IsNullOrEmpty(token.GetString()))
                {
                    next.Add((pending[i].Index, token.GetString()));
                }
            }

            pending = next;
        }

        return ids;
    }

    /// <summary>
    /// Reads an advisory.
    /// </summary>
    /// <returns>The advisory, or null if the database doesn't have it.</returns>
    /// <exception cref="HttpRequestException">The API could not be reached or answered with an error.</exception>
    public virtual async Task<OsvAdvisory> GetAdvisoryOrNullAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        using var client = CreateClient();
        using var response = await client.GetAsync("v1/vulns/" + Uri.EscapeDataString(id), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return OsvAdvisory.Parse(json.RootElement);
    }

    private static JsonObject CreateQuery(OsvQuery query, string pageToken)
    {
        var json = new JsonObject
        {
            ["package"] = new JsonObject { ["ecosystem"] = query.Ecosystem, ["name"] = query.Name },
            ["version"] = query.Version,
        };
        if (pageToken != null)
        {
            json["page_token"] = pageToken;
        }

        return json;
    }

    private HttpClient CreateClient()
    {
        var options = _options.Value;
        var baseUrl = string.IsNullOrEmpty(options.ApiUrl) ? VulnerabilityScanningOptions.DefaultApiUrl : options.ApiUrl;
        var client = new HttpClient(_handler, disposeHandler: false)
        {
            BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PaGetto");

        return client;
    }
}
