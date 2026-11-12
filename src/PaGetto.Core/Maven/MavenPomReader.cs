using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PaGetto.Core.Artifacts;

namespace PaGetto.Core.Maven;

/// <summary>
/// Reads the parts of a POM (<c>pom.xml</c>) that the server stores as a version's manifest: the
/// coordinates, packaging, name, description, links, licenses and dependencies. The manifest is a
/// JSON object with these fields; <see cref="MavenPomSummary"/> reads it back.
/// </summary>
public static partial class MavenPomReader
{
    // Real POMs are a few KiB; a larger one is not read, so that an upload can't use up memory.
    private const long MaxPomBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Reads a POM.
    /// </summary>
    /// <param name="pom">The POM. It is read from its current position.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="InvalidMavenPomException">The POM is not a readable XML document.</exception>
    public static JsonObject Read(Stream pom)
    {
        XDocument document;
        try
        {
            var limited = new LimitedReadStream(pom, MaxPomBytes, () => new InvalidMavenPomException("The POM is too large."));
            using var reader = XmlReader.Create(limited, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            });

            document = XDocument.Load(reader);
        }
        catch (XmlException e)
        {
            throw new InvalidMavenPomException("The POM is not valid XML.", e);
        }

        var project = document.Root;
        if (project?.Name.LocalName != "project")
        {
            throw new InvalidMavenPomException("The POM has no project element.");
        }

        var parent = Child(project, "parent");

        var manifest = new JsonObject
        {
            // A POM may leave out what it inherits from its parent.
            ["groupId"] = Text(project, "groupId") ?? Text(parent, "groupId"),
            ["artifactId"] = Text(project, "artifactId"),
            ["version"] = Text(project, "version") ?? Text(parent, "version"),
            ["packaging"] = Text(project, "packaging") ?? "jar",
            ["name"] = Text(project, "name"),
            ["description"] = Text(project, "description"),
            ["url"] = Text(project, "url"),
            ["scmUrl"] = Text(Child(project, "scm"), "url"),
            ["issuesUrl"] = Text(Child(project, "issueManagement"), "url"),
        };

        if (parent != null)
        {
            manifest["parent"] = new JsonObject
            {
                ["groupId"] = Text(parent, "groupId"),
                ["artifactId"] = Text(parent, "artifactId"),
                ["version"] = Text(parent, "version"),
            };
        }

        var licenses = new JsonArray();
        foreach (var license in Children(Child(project, "licenses"), "license"))
        {
            var name = Text(license, "name");
            if (name != null)
            {
                licenses.Add(name);
            }
        }

        manifest["licenses"] = licenses;

        var dependencies = new JsonArray();
        foreach (var dependency in Children(Child(project, "dependencies"), "dependency"))
        {
            dependencies.Add(new JsonObject
            {
                ["groupId"] = Text(dependency, "groupId"),
                ["artifactId"] = Text(dependency, "artifactId"),
                ["version"] = Text(dependency, "version"),
                ["scope"] = Text(dependency, "scope"),
                ["optional"] = string.Equals(Text(dependency, "optional"), "true", StringComparison.OrdinalIgnoreCase),
            });
        }

        manifest["dependencies"] = dependencies;

        return manifest;
    }

    /// <summary>
    /// The manifest of a version whose POM the server doesn't have (yet): just its coordinates.
    /// </summary>
    public static JsonObject CreateManifest(string groupId, string artifactId, string version)
    {
        return new JsonObject
        {
            ["groupId"] = groupId,
            ["artifactId"] = artifactId,
            ["version"] = version,
        };
    }

    /// <summary>
    /// The description shown for a version: the POM's description, or else its name.
    /// </summary>
    /// <returns>The description on one line, or null.</returns>
    public static string GetDescription(JsonObject manifest, int maxLength)
    {
        var text = GetString(manifest, "description") ?? GetString(manifest, "name");
        if (text == null)
        {
            return null;
        }

        // POM descriptions are usually indented over several lines.
        text = WhitespaceRegex().Replace(text, " ").Trim();

        return text.Length > maxLength ? text[..maxLength] : text;
    }

    /// <summary>
    /// A string field of a manifest, or null.
    /// </summary>
    public static string GetString(JsonObject manifest, string field)
    {
        return manifest?[field] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    private static XElement Child(XElement element, string name)
    {
        return element?.Elements().FirstOrDefault(e => e.Name.LocalName == name);
    }

    private static IEnumerable<XElement> Children(XElement element, string name)
    {
        return element?.Elements().Where(e => e.Name.LocalName == name) ?? [];
    }

    private static string Text(XElement element, string name)
    {
        var value = Child(element, name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
