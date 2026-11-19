using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaGetto.Core.MaliciousPackages;

/// <summary>
/// The versions of a package that a malicious package advisory affects: the versions it lists and
/// its version ranges (see <see href="https://ossf.github.io/osv-schema/#affectedranges-field"/>).
/// They are stored as JSON, <c>{"versions":[…],"ranges":[[{"introduced":"1.0.0"},{"fixed":"1.2.0"}]]}</c>,
/// or as null when every version is affected, which is what most advisories say.
/// </summary>
public static class MaliciousVersions
{
    private static readonly string[] EventKinds = ["introduced", "fixed", "last_affected", "limit"];

    /// <summary>
    /// Reads the affected versions of an <c>affected</c> entry of an advisory.
    /// </summary>
    /// <returns>The versions to store, or null when every version is affected.</returns>
    public static string Read(JsonElement affected)
    {
        var versions = new List<string>();
        if (affected.TryGetProperty("versions", out var versionsJson) && versionsJson.ValueKind == JsonValueKind.Array)
        {
            versions.AddRange(versionsJson.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString()))
                .Select(v => v.GetString()));
        }

        var ranges = new List<List<(string Kind, string Version)>>();
        if (affected.TryGetProperty("ranges", out var rangesJson) && rangesJson.ValueKind == JsonValueKind.Array)
        {
            foreach (var range in rangesJson.EnumerateArray())
            {
                // GIT ranges are commits, not versions.
                if (range.ValueKind != JsonValueKind.Object
                    || (range.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "GIT")
                    || !range.TryGetProperty("events", out var events)
                    || events.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var list = new List<(string Kind, string Version)>();
                foreach (var e in events.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
                {
                    foreach (var kind in EventKinds)
                    {
                        if (e.TryGetProperty(kind, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()))
                        {
                            list.Add((kind, value.GetString()));
                        }
                    }
                }

                // A range that starts at the first version and never ends affects every version.
                if (list.Count > 0 && list.All(e => e.Kind == "introduced" && e.Version == "0"))
                {
                    return null;
                }

                if (list.Count > 0)
                {
                    ranges.Add(list);
                }
            }
        }

        // An entry without versions or ranges names the package as a whole.
        if (versions.Count == 0 && ranges.Count == 0)
        {
            return null;
        }

        return Write(versions, ranges);
    }

    /// <summary>
    /// Whether the stored versions include a version.
    /// </summary>
    /// <param name="affectedVersions">The stored versions; null affects every version.</param>
    /// <param name="version">The version.</param>
    /// <param name="comparer">Orders the versions of the ecosystem.</param>
    public static bool IsAffected(string affectedVersions, string version, IComparer<string> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        if (affectedVersions == null)
        {
            return true;
        }

        if (string.IsNullOrEmpty(version))
        {
            return false;
        }

        JsonNode json;
        try
        {
            json = JsonNode.Parse(affectedVersions);
        }
        catch (JsonException)
        {
            // Unreadable data can't clear a version that the advisory names.
            return true;
        }

        if (json?["versions"] is JsonArray versions
            && versions.Any(v => v is JsonValue value && value.TryGetValue<string>(out var listed) && Compare(comparer, listed, version) == 0))
        {
            return true;
        }

        if (json?["ranges"] is not JsonArray ranges)
        {
            return false;
        }

        foreach (var range in ranges.OfType<JsonArray>())
        {
            var events = range.OfType<JsonObject>()
                .SelectMany(e => e.Select(p => (Kind: p.Key, Version: p.Value is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)))
                .Where(e => e.Version != null)
                .OrderBy(e => e.Version, Comparer<string>.Create((a, b) => CompareEvents(comparer, a, b)))
                .ToList();

            var affected = false;
            foreach (var (kind, eventVersion) in events)
            {
                switch (kind)
                {
                    case "introduced" when eventVersion == "0" || Compare(comparer, version, eventVersion) >= 0:
                        affected = true;
                        break;
                    case "fixed" or "limit" when Compare(comparer, version, eventVersion) >= 0:
                        affected = false;
                        break;
                    case "last_affected" when Compare(comparer, version, eventVersion) > 0:
                        affected = false;
                        break;
                }
            }

            if (affected)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether two version strings are the same version.
    /// </summary>
    public static bool AreSame(string a, string b, IComparer<string> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);

        return a != null && b != null && Compare(comparer, a, b) == 0;
    }

    private static string Write(List<string> versions, List<List<(string Kind, string Version)>> ranges)
    {
        var json = new JsonObject();
        if (versions.Count > 0)
        {
            json["versions"] = new JsonArray(versions.Distinct(StringComparer.Ordinal).Select(v => (JsonNode)v).ToArray());
        }

        if (ranges.Count > 0)
        {
            json["ranges"] = new JsonArray(ranges
                .Select(r => (JsonNode)new JsonArray(r.Select(e => (JsonNode)new JsonObject { [e.Kind] = e.Version }).ToArray()))
                .ToArray());
        }

        return json.ToJsonString();
    }

    // "0" comes before every version.
    private static int CompareEvents(IComparer<string> comparer, string a, string b)
    {
        if (a == "0" || b == "0")
        {
            return (a == "0" ? 0 : 1) - (b == "0" ? 0 : 1);
        }

        return Compare(comparer, a, b);
    }

    // Versions the ecosystem's rules can't read are compared as text.
    private static int Compare(IComparer<string> comparer, string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        try
        {
            return comparer.Compare(a, b);
        }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException)
        {
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
