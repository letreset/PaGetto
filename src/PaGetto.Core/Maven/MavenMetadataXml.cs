using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PaGetto.Core.Maven;

/// <summary>
/// Reads and writes <c>maven-metadata.xml</c> files. The server writes them from what it stores,
/// so the same state always gives the same bytes, and with them the same checksums.
/// See: https://maven.apache.org/repositories/metadata.html
/// </summary>
public static class MavenMetadataXml
{
    private const string TimestampFormat = "yyyyMMddHHmmss";

    private static readonly XmlWriterSettings _writerSettings = new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = true,
        IndentChars = "  ",
    };

    /// <summary>
    /// Reads the metadata of an artifact or group.
    /// </summary>
    /// <exception cref="XmlException">The content is not a metadata document.</exception>
    public static MavenMetadata Read(Stream content)
    {
        using var reader = XmlReader.Create(content, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        });

        var metadata = XDocument.Load(reader).Root;
        if (metadata?.Name.LocalName != "metadata")
        {
            throw new XmlException("The document is not Maven metadata.");
        }

        var versioning = Child(metadata, "versioning");

        return new MavenMetadata
        {
            GroupId = Text(metadata, "groupId"),
            ArtifactId = Text(metadata, "artifactId"),
            Versions = Children(Child(versioning, "versions"), "version")
                .Select(v => v.Value.Trim())
                .Where(v => v.Length > 0)
                .ToList(),
            Plugins = Children(Child(metadata, "plugins"), "plugin")
                .Select(p => new MavenMetadata.Plugin
                {
                    Name = Text(p, "name"),
                    Prefix = Text(p, "prefix"),
                    ArtifactId = Text(p, "artifactId"),
                })
                .Where(p => p.Prefix != null && p.ArtifactId != null)
                .ToList(),
            LastUpdated = DateTime.TryParseExact(
                Text(versioning, "lastUpdated"),
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var lastUpdated)
                ? lastUpdated
                : null,
        };
    }

    /// <summary>
    /// Writes the metadata of an artifact: its versions, the newest one and the newest release.
    /// With plugins, it is (also) the metadata of a group.
    /// </summary>
    /// <param name="groupId">The group id, written when there are versions.</param>
    /// <param name="artifactId">The artifact id, written when there are versions.</param>
    /// <param name="versions">The versions, lowest first.</param>
    /// <param name="lastUpdated">When the versions last changed.</param>
    /// <param name="plugins">The plugins of a group.</param>
    public static byte[] WriteArtifact(
        string groupId,
        string artifactId,
        IReadOnlyList<string> versions,
        DateTime lastUpdated,
        IReadOnlyList<MavenMetadata.Plugin> plugins)
    {
        return Write(writer =>
        {
            writer.WriteStartElement("metadata");

            if (versions.Count > 0)
            {
                writer.WriteElementString("groupId", groupId);
                writer.WriteElementString("artifactId", artifactId);

                writer.WriteStartElement("versioning");
                writer.WriteElementString("latest", versions[^1]);

                var release = versions.LastOrDefault(v => !MavenVersion.IsSnapshot(v));
                if (release != null)
                {
                    writer.WriteElementString("release", release);
                }

                writer.WriteStartElement("versions");
                foreach (var version in versions)
                {
                    writer.WriteElementString("version", version);
                }

                writer.WriteEndElement();
                writer.WriteElementString("lastUpdated", Format(lastUpdated));
                writer.WriteEndElement();
            }

            if (plugins.Count > 0)
            {
                writer.WriteStartElement("plugins");
                foreach (var plugin in plugins)
                {
                    writer.WriteStartElement("plugin");
                    if (plugin.Name != null)
                    {
                        writer.WriteElementString("name", plugin.Name);
                    }

                    writer.WriteElementString("prefix", plugin.Prefix);
                    writer.WriteElementString("artifactId", plugin.ArtifactId);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        });
    }

    /// <summary>
    /// Writes the metadata of a snapshot version: its newest build, and the newest file of each
    /// classifier and extension, which is what clients download for the snapshot.
    /// </summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="artifactId">The artifact id.</param>
    /// <param name="version">The snapshot version, e.g. <c>1.0-SNAPSHOT</c>.</param>
    /// <param name="files">The version's files, with when each was stored.</param>
    public static byte[] WriteSnapshot(
        string groupId,
        string artifactId,
        string version,
        IReadOnlyList<(MavenFileName File, DateTime Stored)> files)
    {
        static (int, string) BuildOrder((MavenFileName File, DateTime Stored) f) => (f.File.SnapshotBuildNumber, f.File.SnapshotTimestamp ?? string.Empty);

        var newestBuild = files
            .Where(f => f.File.SnapshotTimestamp != null)
            .OrderByDescending(BuildOrder)
            .Select(f => f.File)
            .FirstOrDefault();

        var newestFiles = files
            .GroupBy(f => (Classifier: f.File.Classifier?.ToLowerInvariant(), Extension: f.File.Extension.ToLowerInvariant()))
            .Select(g => g.OrderByDescending(BuildOrder).ThenByDescending(f => f.Stored).First())
            .OrderBy(f => f.File.Extension, StringComparer.Ordinal)
            .ThenBy(f => f.File.Classifier ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        return Write(writer =>
        {
            writer.WriteStartElement("metadata");
            writer.WriteAttributeString("modelVersion", "1.1.0");
            writer.WriteElementString("groupId", groupId);
            writer.WriteElementString("artifactId", artifactId);
            writer.WriteElementString("version", version);

            writer.WriteStartElement("versioning");
            if (newestBuild != null)
            {
                writer.WriteStartElement("snapshot");
                writer.WriteElementString("timestamp", newestBuild.SnapshotTimestamp);
                writer.WriteElementString("buildNumber", newestBuild.SnapshotBuildNumber.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }

            writer.WriteElementString("lastUpdated", Format(files.Max(f => f.Stored)));

            writer.WriteStartElement("snapshotVersions");
            foreach (var (file, stored) in newestFiles)
            {
                writer.WriteStartElement("snapshotVersion");
                if (file.Classifier != null)
                {
                    writer.WriteElementString("classifier", file.Classifier);
                }

                writer.WriteElementString("extension", file.Extension);
                writer.WriteElementString("value", file.FileVersion);
                writer.WriteElementString("updated", Format(stored));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        });
    }

    private static byte[] Write(Action<XmlWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, _writerSettings))
        {
            writer.WriteStartDocument();
            write(writer);
            writer.WriteEndDocument();
        }

        return buffer.ToArray();
    }

    private static string Format(DateTime utc)
    {
        return utc.ToString(TimestampFormat, CultureInfo.InvariantCulture);
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
}
