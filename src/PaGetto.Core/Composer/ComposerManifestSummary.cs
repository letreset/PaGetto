using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace PaGetto.Core.Composer;

/// <summary>
/// The parts of a Composer version's manifest (see <see cref="ComposerManifest"/>) that are shown
/// on a package's page.
/// </summary>
public sealed class ComposerManifestSummary
{
    /// <summary>
    /// The package type, e.g. <c>library</c>, or null.
    /// </summary>
    public string Type { get; private init; }

    /// <summary>
    /// The PHP versions the package supports, e.g. <c>&gt;=8.1</c>, or null.
    /// </summary>
    public string Php { get; private init; }

    /// <summary>
    /// The packages it requires, as name and version constraint, without PHP and its extensions.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Dependencies { get; private init; }

    /// <summary>
    /// The links, by the names the package page knows (<c>homepage</c>, <c>repository</c>,
    /// <c>issue_tracker</c>, <c>documentation</c>). Only http and https URLs are kept, so the
    /// links are safe to render.
    /// </summary>
    public IReadOnlyDictionary<string, string> Links { get; private init; }

    public IReadOnlyList<string> Keywords { get; private init; }

    /// <summary>
    /// The licenses, joined, or null.
    /// </summary>
    public string License { get; private init; }

    /// <summary>
    /// Reads a manifest. Fields that are missing or have an unexpected type are skipped, and
    /// anything that is not a JSON object gives an empty summary.
    /// </summary>
    public static ComposerManifestSummary Parse(string manifestJson)
    {
        var manifest = ComposerManifest.Parse(manifestJson);
        var require = manifest?["require"] as JsonObject;

        var dependencies = new List<KeyValuePair<string, string>>();
        foreach (var (name, constraint) in require ?? new JsonObject())
        {
            if (!IsPlatformPackage(name) && constraint is JsonValue value && value.TryGetValue<string>(out var text))
            {
                dependencies.Add(KeyValuePair.Create(name, text));
            }
        }

        var links = new Dictionary<string, string>();
        var support = manifest?["support"] as JsonObject;
        AddLink(links, "homepage", GetString(manifest, "homepage"));
        AddLink(links, "repository", GetString(support, "source"));
        AddLink(links, "issue_tracker", GetString(support, "issues"));
        AddLink(links, "documentation", GetString(support, "docs"));

        // A license is one SPDX identifier or a list of them.
        var licenses = manifest?["license"] switch
        {
            JsonArray items => GetStrings(items),
            JsonValue value when value.TryGetValue<string>(out var license) => [license],
            _ => [],
        };

        return new ComposerManifestSummary
        {
            Type = GetString(manifest, "type"),
            Php = GetString(require, "php"),
            Dependencies = dependencies,
            Links = links,
            Keywords = manifest?["keywords"] is JsonArray keywords ? GetStrings(keywords) : [],
            License = licenses.Count > 0 ? string.Join(", ", licenses) : null,
        };
    }

    /// <summary>
    /// Whether a requirement is of the platform rather than a package: PHP, its extensions and
    /// libraries, and Composer itself.
    /// </summary>
    private static bool IsPlatformPackage(string name)
    {
        return !name.Contains('/', StringComparison.Ordinal);
    }

    private static void AddLink(Dictionary<string, string> links, string name, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            links[name] = uri.AbsoluteUri;
        }
    }

    private static List<string> GetStrings(JsonArray items)
    {
        return items
            .Select(item => item is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null)
            .Where(text => text != null)
            .ToList();
    }

    private static string GetString(JsonObject item, string property)
    {
        return item?[property] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
    }
}
