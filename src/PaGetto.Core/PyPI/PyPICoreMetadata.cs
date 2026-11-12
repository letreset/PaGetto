using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace PaGetto.Core.PyPI;

/// <summary>
/// Reads the core metadata of a distribution (the <c>METADATA</c> file of a wheel, the
/// <c>PKG-INFO</c> file of an sdist), which is in the email header format, into the JSON form PEP
/// 566 defines for it. That JSON, without the description, is the manifest of a version.
/// See: https://packaging.python.org/en/latest/specifications/core-metadata/
/// </summary>
public static class PyPICoreMetadata
{
    // The fields that may appear more than once, as JSON keys. They are JSON arrays.
    private static readonly HashSet<string> MultipleUseFields = new(StringComparer.Ordinal)
    {
        "classifier",
        "dynamic",
        "license_file",
        "obsoletes",
        "obsoletes_dist",
        "platform",
        "project_url",
        "provides",
        "provides_dist",
        "provides_extra",
        "requires",
        "requires_dist",
        "requires_external",
        "supported_platform",
    };

    /// <summary>
    /// Reads core metadata.
    /// </summary>
    /// <param name="text">The metadata.</param>
    /// <param name="description">The long description (the readme), or null if there is none.</param>
    /// <returns>The manifest: the fields as JSON, with lowercase keys whose dashes are underscores.</returns>
    public static JsonObject Read(string text, out string description)
    {
        var manifest = new JsonObject();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        string key = null;
        var value = new StringBuilder();
        var bodyStart = lines.Length;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
            {
                bodyStart = i + 1;
                break;
            }

            if (line[0] is ' ' or '\t')
            {
                // A folded line. Long descriptions in a header are indented with 8 spaces, or 7
                // and a '|' by older tools.
                if (key != null)
                {
                    value.Append('\n').Append(Unfold(line));
                }

                continue;
            }

            Add(manifest, key, value.ToString());

            var colon = line.IndexOf(':');
            key = colon > 0 ? line[..colon].Trim().ToLowerInvariant().Replace('-', '_') : null;
            value.Clear().Append(colon > 0 ? line[(colon + 1)..].Trim() : string.Empty);
        }

        Add(manifest, key, value.ToString());

        // The description is the message body, or a header in older metadata.
        var body = string.Join('\n', lines.Skip(bodyStart)).Trim();
        description = body.Length > 0 ? body : GetString(manifest, "description");
        if (string.IsNullOrWhiteSpace(description))
        {
            description = null;
        }

        manifest.Remove("description");

        // A string in the metadata, and a list in JSON.
        if (GetString(manifest, "keywords") is { } keywords)
        {
            var separators = keywords.Contains(',', StringComparison.Ordinal) ? new[] { ',' } : new[] { ' ', '\t' };
            manifest["keywords"] = new JsonArray(keywords
                .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(k => (JsonNode)JsonValue.Create(k))
                .ToArray());
        }

        return manifest;
    }

    /// <summary>
    /// The manifest of a version whose files have no core metadata.
    /// </summary>
    public static JsonObject CreateManifest(string name, string version)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["version"] = version,
        };
    }

    /// <returns>A string field of a manifest, or null if it is missing or not a string.</returns>
    public static string GetString(JsonObject manifest, string key)
    {
        return manifest?[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
    }

    /// <returns>The strings of a list field of a manifest. Empty if it is missing.</returns>
    public static IReadOnlyList<string> GetStrings(JsonObject manifest, string key)
    {
        return (manifest?[key] as JsonArray ?? [])
            .Select(n => n is JsonValue value && value.TryGetValue<string>(out var text) ? text : null)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
    }

    private static void Add(JsonObject manifest, string key, string value)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (MultipleUseFields.Contains(key))
        {
            if (manifest[key] is not JsonArray list)
            {
                manifest[key] = list = [];
            }

            list.Add(value);
        }
        else if (!manifest.ContainsKey(key))
        {
            // A field that may appear once keeps its first value.
            manifest[key] = value;
        }
    }

    private static string Unfold(string line)
    {
        if (line.StartsWith("       |", StringComparison.Ordinal))
        {
            return line[8..];
        }

        return line.StartsWith("        ", StringComparison.Ordinal) ? line[8..] : line.TrimStart();
    }
}
