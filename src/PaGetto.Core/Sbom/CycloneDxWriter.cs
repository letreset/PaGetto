using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Sbom;

/// <summary>
/// Writes a CycloneDX 1.6 JSON document for a stored version: the version as the described
/// component, the dependencies its manifest declares, and its known vulnerabilities.
/// See: https://cyclonedx.org/docs/1.6/json/
/// </summary>
public static partial class CycloneDxWriter
{
    public const string SpecVersion = "1.6";

    public const string MediaType = "application/vnd.cyclonedx+json";

    /// <summary>
    /// The property that holds the requirement a dependency is declared with, such as a version range.
    /// </summary>
    public const string RequirementProperty = "pagetto:requirement";

    /// <summary>
    /// Writes the document.
    /// </summary>
    /// <param name="package">The version the document describes.</param>
    /// <param name="vulnerabilities">The known vulnerabilities of the version.</param>
    /// <param name="serialNumber">The document's unique serial number.</param>
    /// <param name="timestamp">When the document was made.</param>
    /// <param name="toolVersion">The version of the server that made it.</param>
    /// <returns>The document as UTF-8 JSON.</returns>
    public static byte[] Write(
        SbomPackage package,
        IReadOnlyList<PackageVulnerability> vulnerabilities,
        Guid serialNumber,
        DateTime timestamp,
        string toolVersion)
    {
        ArgumentNullException.ThrowIfNull(package);

        var rootRef = PackageUrl.Create(package.FeedType, package.Name, package.Version) ?? $"{package.Name}@{package.Version}";

        // A package can be declared more than once, e.g. a NuGet dependency for each target framework.
        var dependencies = new Dictionary<string, (string Name, List<string> Requirements)>(StringComparer.Ordinal);
        foreach (var dependency in package.Dependencies)
        {
            var dependencyRef = PackageUrl.Create(package.FeedType, dependency.Name, null) ?? dependency.Name;
            if (!dependencies.TryGetValue(dependencyRef, out var entry))
            {
                entry = (dependency.Name, []);
                dependencies.Add(dependencyRef, entry);
            }

            if (dependency.Requirement != null && !entry.Requirements.Contains(dependency.Requirement))
            {
                entry.Requirements.Add(dependency.Requirement);
            }
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("bomFormat", "CycloneDX");
            writer.WriteString("specVersion", SpecVersion);
            writer.WriteString("serialNumber", $"urn:uuid:{serialNumber:D}");
            writer.WriteNumber("version", 1);

            writer.WriteStartObject("metadata");
            writer.WriteString("timestamp", DateTime.SpecifyKind(timestamp, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            writer.WriteStartObject("tools");
            writer.WriteStartArray("components");
            writer.WriteStartObject();
            writer.WriteString("type", "application");
            writer.WriteString("name", "PaGetto");
            if (!string.IsNullOrEmpty(toolVersion))
            {
                writer.WriteString("version", toolVersion);
            }

            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            WriteRootComponent(writer, package, rootRef);
            writer.WriteEndObject();

            writer.WriteStartArray("components");
            foreach (var (dependencyRef, (name, requirements)) in dependencies)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "library");
                writer.WriteString("bom-ref", dependencyRef);
                WriteName(writer, package.FeedType, name);
                WriteString(writer, "purl", PackageUrl.Create(package.FeedType, name, null));
                if (requirements.Count > 0)
                {
                    writer.WriteStartArray("properties");
                    foreach (var requirement in requirements)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("name", RequirementProperty);
                        writer.WriteString("value", requirement);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("dependencies");
            writer.WriteStartObject();
            writer.WriteString("ref", rootRef);
            writer.WriteStartArray("dependsOn");
            foreach (var dependencyRef in dependencies.Keys)
            {
                writer.WriteStringValue(dependencyRef);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            WriteVulnerabilities(writer, vulnerabilities ?? [], rootRef, package.Version);

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteRootComponent(Utf8JsonWriter writer, SbomPackage package, string rootRef)
    {
        writer.WriteStartObject("component");
        writer.WriteString("type", "library");
        writer.WriteString("bom-ref", rootRef);
        WriteName(writer, package.FeedType, package.Name);
        writer.WriteString("version", package.Version);
        WriteString(writer, "description", package.Description);
        WriteHashes(writer, md5: null, sha1: null, package.Sha256, sha512: null);
        WriteLicenses(writer, package.Licenses);
        WriteString(writer, "purl", PackageUrl.Create(package.FeedType, package.Name, package.Version));

        if (package.Files.Count > 0)
        {
            writer.WriteStartArray("components");
            foreach (var file in package.Files)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "file");
                writer.WriteString("bom-ref", $"{rootRef}/{file.FileName}");
                writer.WriteString("name", file.FileName);
                WriteHashes(writer, file.Md5, file.Sha1, file.Sha256, file.Sha512);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    // A Maven or Ivy group, an npm scope and a Composer vendor are the component's group.
    private static void WriteName(Utf8JsonWriter writer, FeedType feedType, string name)
    {
        PackageUrl.Split(feedType, name, out var ns, out var shortName, out _);
        if (ns != null && feedType is FeedType.Maven or FeedType.Ivy or FeedType.Npm or FeedType.Composer)
        {
            writer.WriteString("group", ns);
            writer.WriteString("name", shortName);
        }
        else
        {
            writer.WriteString("name", name);
        }
    }

    private static void WriteHashes(Utf8JsonWriter writer, string md5, string sha1, string sha256, string sha512)
    {
        var hashes = new[] { ("MD5", md5), ("SHA-1", sha1), ("SHA-256", sha256), ("SHA-512", sha512) }
            .Where(h => h.Item2 != null && HexRegex().IsMatch(h.Item2))
            .ToList();
        if (hashes.Count == 0)
        {
            return;
        }

        writer.WriteStartArray("hashes");
        foreach (var (algorithm, content) in hashes)
        {
            writer.WriteStartObject();
            writer.WriteString("alg", algorithm);
            writer.WriteString("content", content.ToLowerInvariant());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    // CycloneDX takes either one SPDX expression or a list of licenses. A single declaration that
    // reads as an expression (MIT, Apache-2.0 OR MIT) is one; anything else is a license name.
    private static void WriteLicenses(Utf8JsonWriter writer, IReadOnlyList<string> licenses)
    {
        var declared = licenses.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
        if (declared.Count == 0)
        {
            return;
        }

        writer.WriteStartArray("licenses");
        if (declared.Count == 1 && SpdxExpressionRegex().IsMatch(declared[0]))
        {
            writer.WriteStartObject();
            writer.WriteString("expression", declared[0]);
            writer.WriteEndObject();
        }
        else
        {
            foreach (var license in declared)
            {
                writer.WriteStartObject();
                writer.WriteStartObject("license");
                writer.WriteString("name", license);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
    }

    private static void WriteVulnerabilities(Utf8JsonWriter writer, IReadOnlyList<PackageVulnerability> vulnerabilities, string rootRef, string version)
    {
        if (vulnerabilities.Count == 0)
        {
            return;
        }

        writer.WriteStartArray("vulnerabilities");
        foreach (var vulnerability in vulnerabilities)
        {
            writer.WriteStartObject();
            writer.WriteString("id", vulnerability.AdvisoryId);
            WriteOsvSource(writer, "source", vulnerability.AdvisoryId);

            var aliases = (vulnerability.Aliases ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (aliases.Length > 0)
            {
                writer.WriteStartArray("references");
                foreach (var alias in aliases)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", alias);
                    WriteOsvSource(writer, "source", alias);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteStartArray("ratings");
            writer.WriteStartObject();
            if (vulnerability.CvssScore is { } score)
            {
                writer.WriteNumber("score", score);
            }

            writer.WriteString("severity", GetSeverity(vulnerability.Severity));
            if (vulnerability.CvssScore != null)
            {
                writer.WriteString("method", "CVSSv3");
            }

            writer.WriteEndObject();
            writer.WriteEndArray();

            WriteString(writer, "description", vulnerability.Summary);
            if (!string.IsNullOrEmpty(vulnerability.FixedVersion))
            {
                writer.WriteString("recommendation", $"Upgrade to {vulnerability.FixedVersion}.");
            }

            writer.WriteStartArray("affects");
            writer.WriteStartObject();
            writer.WriteString("ref", rootRef);
            writer.WriteStartArray("versions");
            writer.WriteStartObject();
            writer.WriteString("version", version);
            writer.WriteString("status", "affected");
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteOsvSource(Utf8JsonWriter writer, string propertyName, string id)
    {
        writer.WriteStartObject(propertyName);
        writer.WriteString("name", "OSV");
        writer.WriteString("url", $"https://osv.dev/vulnerability/{Uri.EscapeDataString(id)}");
        writer.WriteEndObject();
    }

    private static string GetSeverity(VulnerabilitySeverity severity)
    {
        return severity switch
        {
            VulnerabilitySeverity.Low => "low",
            VulnerabilitySeverity.Moderate => "medium",
            VulnerabilitySeverity.High => "high",
            VulnerabilitySeverity.Critical => "critical",
            _ => "unknown",
        };
    }

    private static void WriteString(Utf8JsonWriter writer, string propertyName, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            writer.WriteString(propertyName, value);
        }
    }

    // The hash lengths CycloneDX accepts: MD5, SHA-1, SHA-256, SHA-384 and SHA-512.
    [GeneratedRegex("^([a-fA-F0-9]{32}|[a-fA-F0-9]{40}|[a-fA-F0-9]{64}|[a-fA-F0-9]{96}|[a-fA-F0-9]{128})$", RegexOptions.CultureInvariant)]
    private static partial Regex HexRegex();

    // License ids joined by AND, OR or WITH, optionally in parentheses, e.g. "(MIT OR Apache-2.0)".
    [GeneratedRegex(@"^\(*[A-Za-z0-9.+:-]+\)*(\s+(AND|OR|WITH)\s+\(*[A-Za-z0-9.+:-]+\)*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SpdxExpressionRegex();
}
