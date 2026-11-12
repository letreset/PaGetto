using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PaGetto.Core.Npm;

/// <summary>
/// The parts of an npm package manifest (<c>package.json</c>) that are shown on a package's page.
/// </summary>
public sealed partial class NpmManifestSummary
{
    /// <summary>
    /// The package's links, by their manifest field (<c>homepage</c>, <c>repository</c>,
    /// <c>bugs</c>). Only http and https URLs are kept, so the links are safe to render.
    /// </summary>
    public IReadOnlyDictionary<string, string> Links { get; private init; }

    /// <summary>
    /// The <c>engines</c>, e.g. <c>node</c>.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Engines { get; private init; }

    /// <summary>
    /// The <c>dependencies</c>, as package name and version range.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Dependencies { get; private init; }

    public IReadOnlyList<string> Keywords { get; private init; }

    /// <summary>
    /// The license expression, or null.
    /// </summary>
    public string License { get; private init; }

    /// <summary>
    /// The deprecation message, or null.
    /// </summary>
    public string Deprecated { get; private init; }

    /// <summary>
    /// Reads a manifest JSON object. Fields that are missing or have an unexpected type are skipped,
    /// and anything that is not a JSON object gives an empty summary.
    /// </summary>
    public static NpmManifestSummary Parse(string manifestJson)
    {
        JsonObject manifest = null;
        try
        {
            manifest = string.IsNullOrEmpty(manifestJson) ? null : JsonNode.Parse(manifestJson) as JsonObject;
        }
        catch (JsonException)
        {
        }

        var links = new Dictionary<string, string>();
        AddLink(links, "homepage", GetString(manifest?["homepage"]));
        AddLink(links, "repository", ToRepositoryUrl(GetUrl(manifest?["repository"])));
        AddLink(links, "bugs", GetUrl(manifest?["bugs"]));

        return new NpmManifestSummary
        {
            Links = links,
            Engines = GetEntries(manifest?["engines"]),
            Dependencies = GetEntries(manifest?["dependencies"]),
            Keywords = manifest?["keywords"] is JsonArray keywords
                ? keywords.Select(GetString).Where(k => !string.IsNullOrWhiteSpace(k)).ToList()
                : [],
            License = GetString(manifest?["license"]) ?? GetString((manifest?["license"] as JsonObject)?["type"]),
            Deprecated = GetString(manifest?["deprecated"]),
        };
    }

    private static void AddLink(Dictionary<string, string> links, string field, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            links[field] = uri.AbsoluteUri;
        }
    }

    /// <summary>
    /// The <c>repository</c> and <c>bugs</c> fields are a URL, or an object with a <c>url</c>.
    /// </summary>
    private static string GetUrl(JsonNode node)
    {
        return GetString(node) ?? GetString((node as JsonObject)?["url"]);
    }

    /// <summary>
    /// Turns the repository forms of package.json into a web URL: <c>git+https://host/a/b.git</c>,
    /// <c>github:a/b</c> and the GitHub shorthand <c>a/b</c>.
    /// </summary>
    private static string ToRepositoryUrl(string repository)
    {
        if (string.IsNullOrEmpty(repository))
        {
            return null;
        }

        if (repository.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
        {
            repository = repository["github:".Length..];
        }

        if (ShorthandRegex().IsMatch(repository))
        {
            return "https://github.com/" + repository;
        }

        if (repository.StartsWith("git+", StringComparison.OrdinalIgnoreCase))
        {
            repository = repository["git+".Length..];
        }

        return repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? repository[..^".git".Length] : repository;
    }

    private static List<KeyValuePair<string, string>> GetEntries(JsonNode node)
    {
        if (node is not JsonObject map)
        {
            return [];
        }

        return map
            .Select(entry => KeyValuePair.Create(entry.Key, GetString(entry.Value)))
            .Where(entry => entry.Value != null)
            .ToList();
    }

    private static string GetString(JsonNode node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ShorthandRegex();
}
