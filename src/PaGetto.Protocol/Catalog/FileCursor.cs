using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PaGetto.Protocol.Catalog;

/// <summary>
/// A cursor implementation which stores the cursor in local file.<br/>
/// The cursor value is written to the file as a JSON object.
/// </summary>
public partial class FileCursor : ICursor
{
    private readonly string _path;
    private readonly ILogger<FileCursor> _logger;

    public FileCursor(string path, ILogger<FileCursor> logger)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DateTimeOffset?> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var file = File.OpenRead(_path);
            var data = await JsonSerializer.DeserializeAsync<Data>(file, options: null, cancellationToken);
            LogCursorRead(data.Value, _path);
            return data.Value;
        }
        catch (Exception e) when (e is FileNotFoundException || e is JsonException)
        {
            return null;
        }
    }

    public Task SetAsync(DateTimeOffset value, CancellationToken cancellationToken)
    {
        var data = new Data { Value = value };
        var jsonString = JsonSerializer.Serialize(data);
        File.WriteAllText(_path, jsonString);
        LogCursorWritten(data.Value, _path);
        return Task.CompletedTask;
    }

    private class Data
    {
        [JsonPropertyName("value")]
        public DateTimeOffset Value { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Read cursor value {cursor:O} from {path}.")]
    private partial void LogCursorRead(DateTimeOffset cursor, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Wrote cursor value {cursor:O} to {path}.")]
    private partial void LogCursorWritten(DateTimeOffset cursor, string path);
}
