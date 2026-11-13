using System;
using System.Collections.Generic;
using System.Linq;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// The parts of a gem version's manifest the web UI shows, from the gemspec of its <c>ruby</c>
/// platform, or else of its first platform.
/// </summary>
public sealed class RubyGemsManifestSummary
{
    // Gemspec metadata links, by the field names the package page labels.
    private static readonly (string MetadataKey, string Field)[] _metadataLinks =
    [
        ("source_code_uri", "repository"),
        ("documentation_uri", "documentation"),
        ("changelog_uri", "Changelog"),
        ("bug_tracker_uri", "issue_tracker"),
    ];

    /// <summary>
    /// The gem name as published.
    /// </summary>
    public string Name { get; private init; }

    /// <summary>
    /// The Ruby versions the gem works with, e.g. <c>&gt;= 3.0</c>, or null for any.
    /// </summary>
    public string RequiredRubyVersion { get; private init; }

    /// <summary>
    /// The homepage and the links of the gemspec's metadata, by field name.
    /// </summary>
    public IReadOnlyDictionary<string, string> Links { get; private init; }

    /// <summary>
    /// The runtime dependencies, each with its requirement.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Dependencies { get; private init; }

    public string License { get; private init; }

    public IReadOnlyList<string> Authors { get; private init; }

    /// <summary>
    /// The platforms the version has a gem file for, <c>ruby</c> first.
    /// </summary>
    public IReadOnlyList<string> Platforms { get; private init; }

    /// <summary>
    /// Reads a manifest. Anything that is not one gives an empty summary.
    /// </summary>
    public static RubyGemsManifestSummary Parse(string manifestJson)
    {
        var manifest = RubyGemsManifest.Parse(manifestJson);
        var main = manifest.Main ?? new RubyGemsSpecification();

        var links = new Dictionary<string, string>();
        AddLink(links, "homepage", main.Homepage ?? main.Metadata.GetValueOrDefault("homepage_uri"));
        foreach (var (metadataKey, field) in _metadataLinks)
        {
            AddLink(links, field, main.Metadata.GetValueOrDefault(metadataKey));
        }

        return new RubyGemsManifestSummary
        {
            Name = manifest.Name,
            RequiredRubyVersion = main.RequiredRubyVersion.Count > 0 ? string.Join(", ", main.RequiredRubyVersion) : null,
            Links = links,
            Dependencies = main.Dependencies
                .Where(d => d.Type == RubyGemsDependency.RuntimeType)
                .Select(d => KeyValuePair.Create(d.Name, d.Requirement.Count > 0 ? string.Join(", ", d.Requirement) : ">= 0"))
                .ToList(),
            License = main.Licenses.Count > 0 ? string.Join(", ", main.Licenses) : null,
            Authors = main.Authors,
            Platforms = manifest.Gems.Select(g => g.Platform ?? RubyGemsFileName.RubyPlatform).ToList(),
        };
    }

    private static void AddLink(Dictionary<string, string> links, string field, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            links.TryAdd(field, uri.AbsoluteUri);
        }
    }
}
