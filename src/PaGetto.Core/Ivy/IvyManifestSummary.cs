using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaGetto.Core.Ivy;

/// <summary>
/// The parts of an Ivy revision's manifest (read from its <c>ivy.xml</c> by
/// <see cref="IvyDescriptorReader"/>) that are shown in the web UI.
/// </summary>
public sealed class IvyManifestSummary
{
    /// <summary>
    /// The organisation as it was published. Null when the manifest has none.
    /// </summary>
    public string Organisation { get; private init; }

    /// <summary>
    /// The module as it was published. Null when the manifest has none.
    /// </summary>
    public string Module { get; private init; }

    /// <summary>
    /// The Scala version of an sbt plugin, or null.
    /// </summary>
    public string ScalaVersion { get; private init; }

    /// <summary>
    /// The sbt version of an sbt plugin, or null.
    /// </summary>
    public string SbtVersion { get; private init; }

    /// <summary>
    /// The status, e.g. <c>release</c> or <c>integration</c>. Null until the descriptor is stored.
    /// </summary>
    public string Status { get; private init; }

    /// <summary>
    /// The links, by the names the package page knows. Only http and https URLs are kept, so the
    /// links are safe to render.
    /// </summary>
    public IReadOnlyDictionary<string, string> Links { get; private init; }

    /// <summary>
    /// The dependencies, as their names in the form of <see cref="IvyPath.GetDisplayName"/> and their
    /// revision and configuration mapping.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Dependencies { get; private init; }

    /// <summary>
    /// The license names, joined, or null.
    /// </summary>
    public string License { get; private init; }

    /// <summary>
    /// Whether the module is an sbt plugin, which is published per sbt version.
    /// </summary>
    public bool IsSbtPlugin => SbtVersion != null;

    /// <summary>
    /// The name as published, in the form of <see cref="IvyPath.GetDisplayName"/>, or null when the
    /// manifest has no coordinates.
    /// </summary>
    public string Name => Organisation != null && Module != null
        ? IvyPath.GetDisplayName(Organisation, Module, ScalaVersion, SbtVersion)
        : null;

    /// <summary>
    /// Reads a manifest. Fields that are missing or have an unexpected type are skipped, and
    /// anything that is not a JSON object gives an empty summary.
    /// </summary>
    public static IvyManifestSummary Parse(string manifestJson)
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
        if (Uri.TryCreate(IvyDescriptorReader.GetString(manifest, "homepage"), UriKind.Absolute, out var homepage)
            && (homepage.Scheme == Uri.UriSchemeHttp || homepage.Scheme == Uri.UriSchemeHttps))
        {
            links["homepage"] = homepage.AbsoluteUri;
        }

        var dependencies = new List<KeyValuePair<string, string>>();
        foreach (var dependency in (manifest?["dependencies"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var org = IvyDescriptorReader.GetString(dependency, "org");
            var name = IvyDescriptorReader.GetString(dependency, "name");
            if (org == null || name == null)
            {
                continue;
            }

            var details = new[] { IvyDescriptorReader.GetString(dependency, "rev"), IvyDescriptorReader.GetString(dependency, "conf") };
            var key = IvyPath.GetDisplayName(
                org,
                name,
                IvyDescriptorReader.GetString(dependency, "scalaVersion"),
                IvyDescriptorReader.GetString(dependency, "sbtVersion"));

            dependencies.Add(KeyValuePair.Create(key, string.Join(" · ", details.Where(d => d != null))));
        }

        var licenses = (manifest?["licenses"] as JsonArray ?? [])
            .Select(l => l is JsonValue value && value.TryGetValue<string>(out var text) ? text : null)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        return new IvyManifestSummary
        {
            Organisation = IvyDescriptorReader.GetString(manifest, "organisation"),
            Module = IvyDescriptorReader.GetString(manifest, "module"),
            ScalaVersion = IvyDescriptorReader.GetString(manifest, "scalaVersion"),
            SbtVersion = IvyDescriptorReader.GetString(manifest, "sbtVersion"),
            Status = IvyDescriptorReader.GetString(manifest, "status"),
            Links = links,
            Dependencies = dependencies,
            License = licenses.Count == 0 ? null : string.Join(", ", licenses),
        };
    }
}
