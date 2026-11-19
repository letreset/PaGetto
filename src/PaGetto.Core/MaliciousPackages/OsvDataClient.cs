using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PaGetto.Core.Configuration;

namespace PaGetto.Core.MaliciousPackages;

/// <summary>
/// Reads the malicious package advisories of the OSV data export
/// (<see cref="MaliciousPackageOptions.DataUrl"/>): every advisory of an ecosystem from its
/// <c>all.zip</c>, the advisories changed since a time from its <c>modified_id.csv</c>, and a
/// single advisory from its <c>{id}.json</c>.
/// </summary>
public class OsvDataClient
{
    // Pooled connections and no cookies, like the upstream requests.
    private static readonly SocketsHttpHandler _sharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip,
    };

    private readonly IOptionsMonitor<MaliciousPackageOptions> _options;
    private readonly HttpMessageHandler _handler;

    /// <param name="options">The export's URL and timeout.</param>
    /// <param name="handlerOverride">The handler requests are sent with; tests send them to a fake.</param>
    public OsvDataClient(IOptionsMonitor<MaliciousPackageOptions> options, HttpMessageHandler handlerOverride = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _handler = handlerOverride ?? _sharedHandler;
    }

    /// <summary>
    /// Reads every malicious package advisory of an ecosystem, withdrawn ones included.
    /// </summary>
    /// <exception cref="HttpRequestException">The export could not be reached or answered with an error.</exception>
    public virtual async IAsyncEnumerable<MaliciousAdvisory> GetAllAsync(string ecosystem, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The archive is large (over 200 MB for npm), so it is read from a temporary file.
        var path = Path.GetTempFileName();
        try
        {
            using (var client = CreateClient())
            using (var response = await client.GetAsync(GetPath(ecosystem, "all.zip"), HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(path);
                await response.Content.CopyToAsync(file, cancellationToken);
            }

            await using var zipFile = File.OpenRead(path);
            using var zip = new ZipArchive(zipFile, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MaliciousAdvisory.IsMaliciousId(entry.Name) || !entry.Name.EndsWith(".json", StringComparison.Ordinal))
                {
                    continue;
                }

                MaliciousAdvisory advisory;
                await using (var stream = entry.Open())
                {
                    advisory = await ReadAdvisoryOrNullAsync(stream, cancellationToken);
                }

                if (advisory != null)
                {
                    yield return advisory;
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Finds the malicious package advisories of an ecosystem changed after a time.
    /// </summary>
    /// <param name="ecosystem">The OSV ecosystem.</param>
    /// <param name="afterUtc">The time.</param>
    /// <param name="max">The most advisories to return; when more changed, the result says so instead.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <exception cref="HttpRequestException">The export could not be reached or answered with an error.</exception>
    public virtual async Task<OsvDataChanges> GetChangesAsync(string ecosystem, DateTime afterUtc, int max, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(GetPath(ecosystem, "modified_id.csv"), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Newest first, "{modified},{id}" per line, so reading stops at the first older change.
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
        DateTime? newest = null;
        var ids = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var comma = line.IndexOf(',');
            if (comma < 0 || !MaliciousAdvisory.TryParseTimestamp(line[..comma], out var modified))
            {
                continue;
            }

            if (modified <= afterUtc)
            {
                break;
            }

            newest ??= modified;
            var id = line[(comma + 1)..].Trim();
            if (MaliciousAdvisory.IsMaliciousId(id))
            {
                if (ids.Count == max)
                {
                    return new OsvDataChanges(newest, ids, TooMany: true);
                }

                ids.Add(id);
            }
        }

        return new OsvDataChanges(newest, ids, TooMany: false);
    }

    /// <summary>
    /// Reads a malicious package advisory.
    /// </summary>
    /// <returns>The advisory, or null if the export doesn't have it.</returns>
    /// <exception cref="HttpRequestException">The export could not be reached or answered with an error.</exception>
    public virtual async Task<MaliciousAdvisory> GetAdvisoryOrNullAsync(string ecosystem, string id, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(GetPath(ecosystem, id + ".json"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        return await ReadAdvisoryOrNullAsync(stream, cancellationToken);
    }

    private static async Task<MaliciousAdvisory> ReadAdvisoryOrNullAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return MaliciousAdvisory.ParseOrNull(json.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetPath(string ecosystem, string fileName)
    {
        return Uri.EscapeDataString(ecosystem) + "/" + Uri.EscapeDataString(fileName);
    }

    private HttpClient CreateClient()
    {
        var options = _options.CurrentValue;
        var baseUrl = string.IsNullOrEmpty(options.DataUrl) ? MaliciousPackageOptions.DefaultDataUrl : options.DataUrl;
        var client = new HttpClient(_handler, disposeHandler: false)
        {
            BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PaGetto");

        return client;
    }
}
