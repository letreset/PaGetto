using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaGetto.Core.PyPI;

/// <summary>
/// The parts of a Python version's manifest (its core metadata, read by <see cref="PyPICoreMetadata"/>)
/// that are shown on a package's page.
/// </summary>
public sealed class PyPIManifestSummary
{
    // A license field longer than this is the license text rather than its name.
    private const int MaxLicenseNameLength = 80;

    /// <summary>
    /// The project name as it was published, or null when the manifest has none.
    /// </summary>
    public string Name { get; private init; }

    /// <summary>
    /// The Python versions the version supports, e.g. <c>&gt;=3.9</c>, or null.
    /// </summary>
    public string RequiresPython { get; private init; }

    /// <summary>
    /// The links, by the names the package page knows (<c>homepage</c>, <c>repository</c>,
    /// <c>issue_tracker</c>, <c>documentation</c>) or by their label in the metadata. Only http and
    /// https URLs are kept, so the links are safe to render.
    /// </summary>
    public IReadOnlyDictionary<string, string> Links { get; private init; }

    /// <summary>
    /// The requirements (<c>Requires-Dist</c>), as project name and the rest of the requirement:
    /// extras, version specifiers and environment markers.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Dependencies { get; private init; }

    public IReadOnlyList<string> Keywords { get; private init; }

    /// <summary>
    /// The license expression or name, or null.
    /// </summary>
    public string License { get; private init; }

    /// <summary>
    /// Reads a manifest. Fields that are missing or have an unexpected type are skipped, and
    /// anything that is not a JSON object gives an empty summary.
    /// </summary>
    public static PyPIManifestSummary Parse(string manifestJson)
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
        AddLink(links, "homepage", PyPICoreMetadata.GetString(manifest, "home_page"));
        foreach (var projectUrl in PyPICoreMetadata.GetStrings(manifest, "project_url"))
        {
            // "Label, URL"
            var comma = projectUrl.IndexOf(',', StringComparison.Ordinal);
            if (comma > 0)
            {
                AddLink(links, GetLinkName(projectUrl[..comma].Trim()), projectUrl[(comma + 1)..].Trim());
            }
        }

        return new PyPIManifestSummary
        {
            Name = PyPICoreMetadata.GetString(manifest, "name"),
            RequiresPython = PyPICoreMetadata.GetString(manifest, "requires_python"),
            Links = links,
            Dependencies = PyPICoreMetadata.GetStrings(manifest, "requires_dist").Select(ParseRequirement).ToList(),
            Keywords = PyPICoreMetadata.GetStrings(manifest, "keywords"),
            License = GetLicense(manifest),
        };
    }

    /// <summary>
    /// Splits a requirement such as <c>requests[socks]&gt;=2.0; python_version &lt; "3.12"</c> into the
    /// project name and the rest.
    /// </summary>
    private static KeyValuePair<string, string> ParseRequirement(string requirement)
    {
        var end = 0;
        while (end < requirement.Length && (char.IsAsciiLetterOrDigit(requirement[end]) || requirement[end] is '.' or '_' or '-'))
        {
            end++;
        }

        var rest = requirement[end..].Trim();

        return KeyValuePair.Create(requirement[..end], rest.Length == 0 ? "any version" : rest);
    }

    private static string GetLicense(JsonObject manifest)
    {
        var expression = PyPICoreMetadata.GetString(manifest, "license_expression");
        if (expression != null)
        {
            return expression;
        }

        var license = PyPICoreMetadata.GetString(manifest, "license")?.Trim();
        if (license != null && license.Length <= MaxLicenseNameLength && !license.Contains('\n', StringComparison.Ordinal))
        {
            return license;
        }

        // "License :: OSI Approved :: MIT License"
        var classifier = PyPICoreMetadata.GetStrings(manifest, "classifier")
            .FirstOrDefault(c => c.StartsWith("License ::", StringComparison.Ordinal));

        return classifier?.Split("::", StringSplitOptions.TrimEntries)[^1];
    }

    private static string GetLinkName(string label)
    {
        // The labels that the core metadata specification suggests, normalized as it says.
        var normalized = new string(label.Where(char.IsAsciiLetterOrDigit).ToArray()).ToLowerInvariant();

        return normalized switch
        {
            "homepage" => "homepage",
            "source" or "repository" or "sourcecode" or "github" => "repository",
            "issues" or "bugtracker" or "issuetracker" or "tracker" or "bugs" => "issue_tracker",
            "documentation" or "docs" => "documentation",
            _ => label,
        };
    }

    private static void AddLink(Dictionary<string, string> links, string field, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            links.TryAdd(field, uri.AbsoluteUri);
        }
    }
}
