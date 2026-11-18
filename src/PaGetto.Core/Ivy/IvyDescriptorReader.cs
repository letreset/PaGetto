using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PaGetto.Core.Artifacts;

namespace PaGetto.Core.Ivy;

/// <summary>
/// Reads the parts of an Ivy descriptor (<c>ivy.xml</c>) that the server stores as a revision's
/// manifest: the module's coordinates with the cross versions of an sbt plugin, the status, the
/// description, homepage and licenses, and the dependencies. The manifest is a JSON object with these
/// fields; <see cref="IvyManifestSummary"/> reads it back.
/// See: https://ant.apache.org/ivy/history/latest-milestone/ivyfile.html
/// </summary>
public static partial class IvyDescriptorReader
{
    // Real descriptors are a few KiB; a larger one is not read, so that an upload can't use up memory.
    private const long MaxDescriptorBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Reads a descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor. It is read from its current position.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="InvalidIvyDescriptorException">The descriptor is not a readable Ivy file.</exception>
    public static JsonObject Read(Stream descriptor)
    {
        XDocument document;
        try
        {
            var limited = new LimitedReadStream(descriptor, MaxDescriptorBytes, () => new InvalidIvyDescriptorException("The ivy.xml is too large."));
            using var reader = XmlReader.Create(limited, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            });

            document = XDocument.Load(reader);
        }
        catch (XmlException e)
        {
            throw new InvalidIvyDescriptorException("The ivy.xml is not valid XML.", e);
        }

        var module = document.Root;
        if (module?.Name.LocalName != "ivy-module")
        {
            throw new InvalidIvyDescriptorException("The ivy.xml has no ivy-module element.");
        }

        var info = Child(module, "info");
        if (info == null)
        {
            throw new InvalidIvyDescriptorException("The ivy.xml has no info element.");
        }

        var description = Child(info, "description");
        var manifest = new JsonObject
        {
            ["organisation"] = Attribute(info, "organisation"),
            ["module"] = Attribute(info, "module"),
            ["revision"] = Attribute(info, "revision"),
            ["scalaVersion"] = Attribute(info, "scalaVersion"),
            ["sbtVersion"] = Attribute(info, "sbtVersion"),
            ["status"] = Attribute(info, "status"),
            ["description"] = string.IsNullOrWhiteSpace(description?.Value) ? null : description.Value.Trim(),
            ["homepage"] = Attribute(description, "homepage"),
        };

        var licenses = new JsonArray();
        foreach (var license in Children(info, "license"))
        {
            if (Attribute(license, "name") is { } name)
            {
                licenses.Add(name);
            }
        }

        manifest["licenses"] = licenses;

        var dependencies = new JsonArray();
        foreach (var dependency in Children(Child(module, "dependencies"), "dependency"))
        {
            dependencies.Add(new JsonObject
            {
                ["org"] = Attribute(dependency, "org"),
                ["name"] = Attribute(dependency, "name"),
                ["rev"] = Attribute(dependency, "rev"),
                ["conf"] = Attribute(dependency, "conf"),
                ["scalaVersion"] = Attribute(dependency, "scalaVersion"),
                ["sbtVersion"] = Attribute(dependency, "sbtVersion"),
            });
        }

        manifest["dependencies"] = dependencies;

        return manifest;
    }

    /// <summary>
    /// The manifest of a revision whose descriptor the server doesn't have (yet): just its coordinates.
    /// </summary>
    public static JsonObject CreateManifest(IvyPath path)
    {
        return new JsonObject
        {
            ["organisation"] = path.Organisation,
            ["module"] = path.Module,
            ["revision"] = path.Revision,
            ["scalaVersion"] = path.ScalaVersion,
            ["sbtVersion"] = path.SbtVersion,
        };
    }

    /// <summary>
    /// Checks that a descriptor is that of the revision it was uploaded to.
    /// </summary>
    /// <exception cref="InvalidIvyDescriptorException">It is the descriptor of another module or revision.</exception>
    public static void CheckCoordinates(JsonObject manifest, IvyPath path)
    {
        var organisation = GetString(manifest, "organisation");
        var module = GetString(manifest, "module");
        var revision = GetString(manifest, "revision");
        var scalaVersion = GetString(manifest, "scalaVersion");
        var sbtVersion = GetString(manifest, "sbtVersion");

        static bool Matches(string descriptorValue, string pathValue) =>
            string.Equals(descriptorValue, pathValue, StringComparison.OrdinalIgnoreCase);

        if (!Matches(organisation, path.Organisation)
            || !Matches(module, path.Module)
            || !Matches(revision, path.Revision)
            || !Matches(scalaVersion, path.ScalaVersion)
            || !Matches(sbtVersion, path.SbtVersion))
        {
            var descriptorName = IvyPath.GetDisplayName(organisation, module, scalaVersion, sbtVersion);
            var pathName = IvyPath.GetDisplayName(path.Organisation, path.Module, path.ScalaVersion, path.SbtVersion);

            throw new InvalidIvyDescriptorException(
                $"The ivy.xml is for {descriptorName} {revision}, but it was uploaded to {pathName} {path.Revision}.");
        }
    }

    /// <summary>
    /// The description shown for a revision.
    /// </summary>
    /// <returns>The description on one line, or null.</returns>
    public static string GetDescription(JsonObject manifest, int maxLength)
    {
        var text = GetString(manifest, "description");
        if (text == null)
        {
            return null;
        }

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

    // Matched by local name, so that the extra attributes of sbt (e:scalaVersion, e:sbtVersion) are
    // found in whatever namespace they are declared.
    private static string Attribute(XElement element, string name)
    {
        var value = element?.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
