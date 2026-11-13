using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaGetto.Core.Cargo;

/// <summary>
/// The parts of a crate version's manifest (see <see cref="CargoIndexEntry"/>) that are shown on a
/// package's page.
/// </summary>
public sealed class CargoManifestSummary
{
    /// <summary>
    /// The crate name as it was published, or null when the manifest has none.
    /// </summary>
    public string Name { get; private init; }

    /// <summary>
    /// The oldest Rust version the crate supports, e.g. <c>1.70</c>, or null.
    /// </summary>
    public string RustVersion { get; private init; }

    /// <summary>
    /// The links, by the names the package page knows (<c>homepage</c>, <c>repository</c>,
    /// <c>documentation</c>). Only http and https URLs are kept, so the links are safe to render.
    /// </summary>
    public IReadOnlyDictionary<string, string> Links { get; private init; }

    /// <summary>
    /// The dependencies other than development dependencies, as crate name and version requirement.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Dependencies { get; private init; }

    public IReadOnlyList<string> Keywords { get; private init; }

    /// <summary>
    /// The license expression, or null.
    /// </summary>
    public string License { get; private init; }

    /// <summary>
    /// Reads a manifest. Fields that are missing or have an unexpected type are skipped, and
    /// anything that is not a JSON object gives an empty summary.
    /// </summary>
    public static CargoManifestSummary Parse(string manifestJson)
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
        foreach (var field in new[] { "homepage", "repository", "documentation" })
        {
            if (Uri.TryCreate(GetString(manifest, field), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                links[field] = uri.AbsoluteUri;
            }
        }

        var dependencies = new List<KeyValuePair<string, string>>();
        if (manifest?["deps"] is JsonArray deps)
        {
            foreach (var item in deps)
            {
                if (item is not JsonObject dependency || GetString(dependency, "kind") == "dev")
                {
                    continue;
                }

                // A renamed dependency names the crate it stands for as "package".
                var name = GetString(dependency, "package") ?? GetString(dependency, "name");
                if (name != null)
                {
                    dependencies.Add(KeyValuePair.Create(name, GetString(dependency, "req") ?? "*"));
                }
            }
        }

        var keywords = new List<string>();
        if (manifest?["keywords"] is JsonArray items)
        {
            foreach (var item in items)
            {
                if (item is JsonValue value && value.TryGetValue<string>(out var keyword) && !string.IsNullOrWhiteSpace(keyword))
                {
                    keywords.Add(keyword);
                }
            }
        }

        return new CargoManifestSummary
        {
            Name = GetString(manifest, "name"),
            RustVersion = GetString(manifest, "rust_version"),
            Links = links,
            Dependencies = dependencies,
            Keywords = keywords,
            License = GetString(manifest, "license"),
        };
    }

    private static string GetString(JsonObject item, string property)
    {
        return item?[property] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
    }
}
