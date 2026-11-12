using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaGetto.Core.Maven;

/// <summary>
/// The parts of a Maven version's manifest (read from its POM by <see cref="MavenPomReader"/>)
/// that are shown on a package's page.
/// </summary>
public sealed class MavenPomSummary
{
    /// <summary>
    /// The group id as it was published. Null when the manifest has none.
    /// </summary>
    public string GroupId { get; private init; }

    /// <summary>
    /// The artifact id as it was published. Null when the manifest has none.
    /// </summary>
    public string ArtifactId { get; private init; }

    /// <summary>
    /// The packaging, e.g. <c>jar</c> or <c>pom</c>. Null until the POM is stored.
    /// </summary>
    public string Packaging { get; private init; }

    /// <summary>
    /// The links, by the names the package page knows (<c>homepage</c>, <c>repository</c>,
    /// <c>issue_tracker</c>). Only http and https URLs are kept, so the links are safe to render.
    /// </summary>
    public IReadOnlyDictionary<string, string> Links { get; private init; }

    /// <summary>
    /// The dependencies, as <c>groupId:artifactId</c> and their version and scope.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Dependencies { get; private init; }

    /// <summary>
    /// The license names, joined, or null.
    /// </summary>
    public string License { get; private init; }

    /// <summary>
    /// The coordinates as published, <c>groupId:artifactId</c>, or null when the manifest has none.
    /// </summary>
    public string Coordinates => GroupId != null && ArtifactId != null ? $"{GroupId}:{ArtifactId}" : null;

    /// <summary>
    /// Reads a manifest. Fields that are missing or have an unexpected type are skipped, and
    /// anything that is not a JSON object gives an empty summary.
    /// </summary>
    public static MavenPomSummary Parse(string manifestJson)
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
        AddLink(links, "homepage", MavenPomReader.GetString(manifest, "url"));
        AddLink(links, "repository", MavenPomReader.GetString(manifest, "scmUrl"));
        AddLink(links, "issue_tracker", MavenPomReader.GetString(manifest, "issuesUrl"));

        var dependencies = new List<KeyValuePair<string, string>>();
        foreach (var dependency in (manifest?["dependencies"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var groupId = MavenPomReader.GetString(dependency, "groupId");
            var artifactId = MavenPomReader.GetString(dependency, "artifactId");
            if (groupId == null || artifactId == null)
            {
                continue;
            }

            // A version left out comes from the dependency management of a parent or BOM.
            var details = new[]
            {
                MavenPomReader.GetString(dependency, "version") ?? "managed",
                MavenPomReader.GetString(dependency, "scope"),
                dependency["optional"] is JsonValue optional && optional.TryGetValue<bool>(out var isOptional) && isOptional ? "optional" : null,
            };

            dependencies.Add(KeyValuePair.Create($"{groupId}:{artifactId}", string.Join(" · ", details.Where(d => d != null))));
        }

        var licenses = (manifest?["licenses"] as JsonArray ?? [])
            .Select(l => l is JsonValue value && value.TryGetValue<string>(out var text) ? text : null)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        return new MavenPomSummary
        {
            GroupId = MavenPomReader.GetString(manifest, "groupId"),
            ArtifactId = MavenPomReader.GetString(manifest, "artifactId"),
            Packaging = MavenPomReader.GetString(manifest, "packaging"),
            Links = links,
            Dependencies = dependencies,
            License = licenses.Count == 0 ? null : string.Join(", ", licenses),
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
}
