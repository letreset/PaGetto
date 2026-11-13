using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaGetto.Core.Go;

/// <summary>
/// The manifest of a module version (<see cref="Entities.ArtifactVersion.MetadataJson"/>): the
/// module path, the version's time and its <c>go.mod</c> file, which together answer the
/// <c>.info</c> and <c>.mod</c> requests of the go command.
/// </summary>
public sealed class GoModuleManifest
{
    private const string ModuleProperty = "module";
    private const string TimeProperty = "time";
    private const string ModProperty = "mod";

    // The properties of an .info file.
    private const string InfoVersionProperty = "Version";
    private const string InfoTimeProperty = "Time";

    /// <summary>
    /// The module path, with its case. Null when the manifest has none.
    /// </summary>
    public string Module { get; private init; }

    /// <summary>
    /// The time of the version: when it was uploaded, or for a version from a mirror, the time the
    /// mirror gives it (usually the time of its commit). Null when the manifest has none.
    /// </summary>
    public DateTime? Time { get; private init; }

    /// <summary>
    /// The content of the version's <c>go.mod</c> file, as the go command downloads it. Null when the manifest has none.
    /// </summary>
    public string Mod { get; private init; }

    public static string Create(string module, DateTime time, string mod)
    {
        return new JsonObject
        {
            [ModuleProperty] = module,
            [TimeProperty] = FormatTime(time),
            [ModProperty] = mod,
        }.ToJsonString();
    }

    /// <summary>
    /// Reads a manifest. Fields that are missing or have an unexpected type are null, and anything
    /// that is not a JSON object gives an empty manifest.
    /// </summary>
    public static GoModuleManifest Parse(string manifestJson)
    {
        JsonObject manifest = null;
        try
        {
            manifest = string.IsNullOrEmpty(manifestJson) ? null : JsonNode.Parse(manifestJson) as JsonObject;
        }
        catch (JsonException)
        {
        }

        DateTime? time = null;
        if (DateTime.TryParse(
            GetString(manifest, TimeProperty),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed))
        {
            time = parsed;
        }

        return new GoModuleManifest
        {
            Module = GetString(manifest, ModuleProperty),
            Time = time,
            Mod = GetString(manifest, ModProperty),
        };
    }

    /// <summary>
    /// The <c>.info</c> of a version: <c>{"Version": "v1.2.3", "Time": "2020-01-01T00:00:00Z"}</c>.
    /// </summary>
    public static string CreateInfo(string version, DateTime time)
    {
        return new JsonObject
        {
            [InfoVersionProperty] = version,
            [InfoTimeProperty] = FormatTime(time),
        }.ToJsonString();
    }

    /// <summary>
    /// A time in RFC 3339 form, in UTC, as the go command expects it.
    /// </summary>
    public static string FormatTime(DateTime time)
    {
        var utc = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : time;
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static string GetString(JsonObject item, string property)
    {
        return item?[property] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrEmpty(text)
            ? text
            : null;
    }
}
