using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaGetto.Core.Pub;

/// <summary>
/// The parts of a pubspec that are shown on a package's page.
/// </summary>
public sealed class PubspecSummary
{
    /// <summary>
    /// The package's links, by their pubspec field (<c>homepage</c>, <c>repository</c>,
    /// <c>issue_tracker</c>, <c>documentation</c>). Only http and https URLs are kept, so the links
    /// are safe to render.
    /// </summary>
    public IReadOnlyDictionary<string, string> Links { get; private init; }

    /// <summary>
    /// The SDK constraints of the <c>environment</c> field, e.g. <c>sdk</c> and <c>flutter</c>.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Environment { get; private init; }

    /// <summary>
    /// The <c>dependencies</c>, as package name and a short description of the constraint.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Dependencies { get; private init; }

    public IReadOnlyList<string> Topics { get; private init; }

    /// <summary>
    /// Reads a pubspec JSON object. Fields that are missing or have an unexpected type are skipped,
    /// and anything that is not a JSON object gives an empty summary.
    /// </summary>
    public static PubspecSummary Parse(string pubspecJson)
    {
        JsonObject pubspec = null;
        try
        {
            pubspec = string.IsNullOrEmpty(pubspecJson) ? null : JsonNode.Parse(pubspecJson) as JsonObject;
        }
        catch (JsonException)
        {
        }

        var links = new Dictionary<string, string>();
        foreach (var field in new[] { "homepage", "repository", "issue_tracker", "documentation" })
        {
            if (Uri.TryCreate(GetString(pubspec?[field]), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                links[field] = uri.AbsoluteUri;
            }
        }

        return new PubspecSummary
        {
            Links = links,
            Environment = GetEntries(pubspec?["environment"], value => GetString(value)),
            Dependencies = GetEntries(pubspec?["dependencies"], DescribeConstraint),
            Topics = pubspec?["topics"] is JsonArray topics
                ? topics.Select(GetString).Where(t => !string.IsNullOrEmpty(t)).ToList()
                : [],
        };
    }

    private static List<KeyValuePair<string, string>> GetEntries(JsonNode node, Func<JsonNode, string> describe)
    {
        if (node is not JsonObject map)
        {
            return [];
        }

        return map
            .Select(entry => KeyValuePair.Create(entry.Key, describe(entry.Value)))
            .Where(entry => entry.Value != null)
            .ToList();
    }

    /// <summary>
    /// A dependency is a version constraint, nothing (any version), or a description of its source.
    /// </summary>
    private static string DescribeConstraint(JsonNode value)
    {
        switch (value)
        {
            case null:
                return "any";

            case JsonObject source:
                if (source.ContainsKey("sdk")) return "SDK " + GetString(source["sdk"]);
                if (source.ContainsKey("git")) return "git";
                if (source.ContainsKey("path")) return "path";
                return GetString(source["version"]) ?? "any";

            default:
                return GetString(value) ?? "any";
        }
    }

    private static string GetString(JsonNode node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }
}
