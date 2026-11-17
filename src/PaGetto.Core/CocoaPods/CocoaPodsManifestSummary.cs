using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaGetto.Core.CocoaPods;

/// <summary>
/// The parts of a pod version's podspec that are shown on a package's page.
/// </summary>
public sealed class CocoaPodsManifestSummary
{
    private static readonly Dictionary<string, string> _platformNames = new(StringComparer.Ordinal)
    {
        ["ios"] = "iOS",
        ["osx"] = "macOS",
        ["macos"] = "macOS",
        ["tvos"] = "tvOS",
        ["watchos"] = "watchOS",
        ["visionos"] = "visionOS",
    };

    /// <summary>
    /// The pod name as published, or null.
    /// </summary>
    public string Name { get; private init; }

    /// <summary>
    /// The platforms the pod supports with their deployment targets (<c>iOS 12.0</c>), and the
    /// Swift versions it builds with.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Platforms { get; private init; }

    /// <summary>
    /// The pods it depends on, as name and requirements.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Dependencies { get; private init; }

    /// <summary>
    /// The links, by the names the package page knows (<c>homepage</c>, <c>repository</c>,
    /// <c>documentation</c>). Only http and https URLs are kept, so the links are safe to render.
    /// </summary>
    public IReadOnlyDictionary<string, string> Links { get; private init; }

    /// <summary>
    /// The license type, e.g. <c>MIT</c>, or null.
    /// </summary>
    public string License { get; private init; }

    /// <summary>
    /// Reads a podspec. Fields that are missing or have an unexpected type are skipped, and anything
    /// that is not a JSON object gives an empty summary.
    /// </summary>
    public static CocoaPodsManifestSummary Parse(string podspecJson)
    {
        JsonObject podspec;
        try
        {
            podspec = JsonNode.Parse(podspecJson ?? "null") as JsonObject;
        }
        catch (JsonException)
        {
            podspec = null;
        }

        var platforms = new List<KeyValuePair<string, string>>();
        foreach (var (platform, target) in podspec?["platforms"] as JsonObject ?? new JsonObject())
        {
            var label = _platformNames.GetValueOrDefault(platform, platform);
            platforms.Add(KeyValuePair.Create(label, GetText(target) ?? "any"));
        }

        var swiftVersions = podspec?["swift_versions"] switch
        {
            JsonArray items => GetStrings(items),
            JsonValue value when value.TryGetValue<string>(out var text) => [text],
            _ => GetString(podspec, "swift_version") is { } swift ? [swift] : [],
        };
        if (swiftVersions.Count > 0)
        {
            platforms.Add(KeyValuePair.Create("Swift", string.Join(", ", swiftVersions)));
        }

        var dependencies = new List<KeyValuePair<string, string>>();
        foreach (var (name, requirements) in podspec?["dependencies"] as JsonObject ?? new JsonObject())
        {
            var text = requirements switch
            {
                JsonArray items => string.Join(", ", GetStrings(items)),
                _ => GetText(requirements) ?? "",
            };
            dependencies.Add(KeyValuePair.Create(name, text));
        }

        var links = new Dictionary<string, string>();
        AddLink(links, "homepage", GetString(podspec, "homepage"));
        AddLink(links, "repository", GetString(podspec?["source"] as JsonObject, "git"));
        AddLink(links, "documentation", GetString(podspec, "documentation_url"));

        // A license is a type, or an object with the type and the license text or file.
        var license = podspec?["license"] switch
        {
            JsonObject item => GetString(item, "type"),
            var value => GetText(value),
        };

        return new CocoaPodsManifestSummary
        {
            Name = GetString(podspec, "name"),
            Platforms = platforms,
            Dependencies = dependencies,
            Links = links,
            License = license,
        };
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
        return items.Select(GetText).Where(text => text != null).ToList();
    }

    private static string GetString(JsonObject item, string property)
    {
        return GetText(item?[property]);
    }

    private static string GetText(JsonNode node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
    }
}
