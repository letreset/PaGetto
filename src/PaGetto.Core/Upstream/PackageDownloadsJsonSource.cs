using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;

namespace PaGetto.Core.Upstream;

public partial class PackageDownloadsJsonSource : IPackageDownloadsSource
{
    public const string PackageDownloadsV1Url = "https://nugetprod0.blob.core.windows.net/ng-search-data/downloads.v1.json";

    private readonly HttpClient _httpClient;
    private readonly ILogger<PackageDownloadsJsonSource> _logger;

    public PackageDownloadsJsonSource(HttpClient httpClient, ILogger<PackageDownloadsJsonSource> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Dictionary<string, Dictionary<string, long>>> GetPackageDownloadsAsync()
    {
        LogFetchingDownloads();

        var results = new Dictionary<string, Dictionary<string, long>>();

        using var downloadsStream = await GetDownloadsStreamAsync();
        using var downloadStreamReader = new StreamReader(downloadsStream);
        using var jsonReader = new JsonTextReader(downloadStreamReader);
        LogParsingDownloads();

        jsonReader.Read();

        while (jsonReader.Read())
        {
            try
            {
                if (jsonReader.TokenType == JsonToken.StartArray)
                {
                    // TODO: This line reads the entire document into memory...
                    var record = JToken.ReadFrom(jsonReader);
                    var id = string.Intern(record[0].ToString().ToLowerInvariant());

                    // The second entry in each record should be an array of versions, if not move on to next entry.
                    // This is a check to safe guard against invalid entries.
                    if (record.Count() == 2 && record[1].Type != JTokenType.Array)
                    {
                        continue;
                    }

                    if (!results.TryGetValue(id, out var value))
                    {
                        value = new Dictionary<string, long>();
                        results.Add(id, value);
                    }

                    foreach (var token in record)
                    {
                        if (token != null && token.Count() == 2)
                        {
                            var version = string.Intern(NuGetVersion.Parse(token[0].ToString()).ToNormalizedString().ToLowerInvariant());
                            var downloads = token[1].ToObject<int>();
                            value[version] = downloads;
                        }
                    }
                }
            }
            catch (JsonReaderException e)
            {
                LogInvalidEntry(e);
            }
        }

        LogParsedDownloads();

        return results;
    }

    private async Task<Stream> GetDownloadsStreamAsync()
    {
        LogDownloadingFile();

        var fileStream = File.Open(Path.GetTempFileName(), FileMode.Create);
        var response = await _httpClient.GetAsync(PackageDownloadsV1Url, HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();

        using (var networkStream = await response.Content.ReadAsStreamAsync())
        {
            await networkStream.CopyToAsync(fileStream);
        }

        fileStream.Seek(0, SeekOrigin.Begin);

        LogDownloadedFile();

        return fileStream;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Fetching package downloads...")]
    private partial void LogFetchingDownloads();

    [LoggerMessage(Level = LogLevel.Information, Message = "Parsing package downloads...")]
    private partial void LogParsingDownloads();

    [LoggerMessage(Level = LogLevel.Error, Message = "Invalid entry in downloads.v1.json")]
    private partial void LogInvalidEntry(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Parsed package downloads")]
    private partial void LogParsedDownloads();

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloading downloads.v1.json...")]
    private partial void LogDownloadingFile();

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded downloads.v1.json")]
    private partial void LogDownloadedFile();
}
