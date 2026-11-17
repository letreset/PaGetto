using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PaGetto.Core.Swift;

/// <summary>
/// The manifest of a stored Swift package release: its identifier and version as published, the
/// metadata of the publisher, the signature of its source archive, its publication time and its
/// <c>Package.swift</c> manifests, from which the registry API answers without opening the archive.
/// See: https://github.com/swiftlang/swift-package-manager/blob/main/Documentation/PackageRegistry/Registry.md
/// </summary>
public sealed partial class SwiftManifest
{
    /// <summary>
    /// The file name of the manifest every package has.
    /// </summary>
    public const string ManifestFileName = "Package.swift";

    /// <summary>
    /// The name of the source archive among the resources of a release.
    /// </summary>
    public const string SourceArchiveResourceName = "source-archive";

    /// <summary>
    /// The signature format Swift Package Manager signs with.
    /// </summary>
    public const string CmsSignatureFormat = "cms-1.0.0";

    /// <summary>
    /// The identifier as published, e.g. <c>mona.LinkedList</c>.
    /// </summary>
    public string Id { get; private init; }

    public string Version { get; private init; }

    /// <summary>
    /// The metadata of the release (description, license, readme and repository URLs, author), an
    /// empty object when the publisher sent none.
    /// </summary>
    public JsonObject Metadata { get; private init; }

    /// <summary>
    /// The signature of the source archive, base64-encoded, or null if it is not signed.
    /// </summary>
    public string Signature { get; private init; }

    /// <summary>
    /// The format of <see cref="Signature"/>, e.g. <c>cms-1.0.0</c>, or null.
    /// </summary>
    public string SignatureFormat { get; private init; }

    /// <summary>
    /// When the release was published, in the ISO 8601 form Swift Package Manager reads.
    /// </summary>
    public string PublishedAt { get; private init; }

    /// <summary>
    /// The manifests by file name: <c>Package.swift</c>, and the version-specific manifests such as
    /// <c>Package@swift-5.7.swift</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Manifests { get; private init; }

    /// <summary>
    /// Whether a file name of a package's root folder is one of its manifests.
    /// </summary>
    public static bool IsManifestFileName(string fileName)
    {
        return fileName == ManifestFileName || VersionSpecificManifestRegex().IsMatch(fileName);
    }

    /// <summary>
    /// Creates the manifest of a release.
    /// </summary>
    public static SwiftManifest Create(
        string id,
        string version,
        JsonObject metadata,
        string signature,
        string signatureFormat,
        DateTime publishedAtUtc,
        IReadOnlyDictionary<string, string> manifests)
    {
        return Create(id, version, metadata, signature, signatureFormat, FormatTime(publishedAtUtc), manifests);
    }

    /// <summary>
    /// Creates the manifest of a release with a publication time as a registry wrote it.
    /// </summary>
    public static SwiftManifest Create(
        string id,
        string version,
        JsonObject metadata,
        string signature,
        string signatureFormat,
        string publishedAt,
        IReadOnlyDictionary<string, string> manifests)
    {
        return new SwiftManifest
        {
            Id = id,
            Version = version,
            Metadata = metadata ?? new JsonObject(),
            Signature = signature,
            SignatureFormat = signature == null ? null : signatureFormat,
            PublishedAt = publishedAt,
            Manifests = manifests,
        };
    }

    /// <summary>
    /// Reads a stored manifest.
    /// </summary>
    /// <returns>The manifest; its fields are null or empty when the JSON lacks them.</returns>
    public static SwiftManifest Parse(string json)
    {
        JsonObject manifest;
        try
        {
            manifest = JsonNode.Parse(json ?? "null") as JsonObject;
        }
        catch (JsonException)
        {
            manifest = null;
        }

        var manifests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (fileName, content) in manifest?["manifests"] as JsonObject ?? new JsonObject())
        {
            if (GetText(content) is { } text)
            {
                manifests[fileName] = text;
            }
        }

        return new SwiftManifest
        {
            Id = GetString(manifest, "id"),
            Version = GetString(manifest, "version"),
            Metadata = manifest?["metadata"]?.DeepClone() as JsonObject ?? new JsonObject(),
            Signature = GetString(manifest, "signature"),
            SignatureFormat = GetString(manifest, "signatureFormat"),
            PublishedAt = GetString(manifest, "publishedAt"),
            Manifests = manifests,
        };
    }

    public string ToJson()
    {
        var manifests = new JsonObject();
        foreach (var (fileName, content) in Manifests.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            manifests[fileName] = content;
        }

        var json = new JsonObject
        {
            ["id"] = Id,
            ["version"] = Version,
            ["metadata"] = Metadata.DeepClone(),
            ["publishedAt"] = PublishedAt,
            ["manifests"] = manifests,
        };

        if (Signature != null)
        {
            json["signature"] = Signature;
            json["signatureFormat"] = SignatureFormat;
        }

        return json.ToJsonString();
    }

    /// <summary>
    /// The release metadata the registry API answers <c>GET /{scope}/{name}/{version}</c> with.
    /// </summary>
    /// <param name="checksum">The SHA-256 of the source archive, in hex.</param>
    public string ToReleaseJson(string checksum)
    {
        var resource = new JsonObject
        {
            ["name"] = SourceArchiveResourceName,
            ["type"] = "application/zip",
            ["checksum"] = checksum,
        };

        if (Signature != null)
        {
            resource["signing"] = new JsonObject
            {
                ["signatureBase64Encoded"] = Signature,
                ["signatureFormat"] = SignatureFormat,
            };
        }

        var release = new JsonObject
        {
            ["id"] = Id,
            ["version"] = Version,
            ["resources"] = new JsonArray(resource),
            ["metadata"] = Metadata.DeepClone(),
        };

        if (PublishedAt != null)
        {
            release["publishedAt"] = PublishedAt;
        }

        return release.ToJsonString();
    }

    /// <summary>
    /// The manifest of <c>GET /{scope}/{name}/{version}/Package.swift?swift-version=</c>: the
    /// version-specific manifest of that Swift version, e.g. <c>Package@swift-5.7.swift</c> for
    /// <c>5.7</c> or <c>5.7.0</c>. Swift Package Manager asks with the tools version an alternate
    /// manifest declares, so a manifest that declares that tools version is taken too.
    /// </summary>
    /// <returns>The file name, or null if the release has no manifest for that version.</returns>
    public string FindVersionSpecificManifest(string swiftVersion)
    {
        if (!TryParseSwiftVersion(swiftVersion, out var requested))
        {
            return null;
        }

        var alternates = GetAlternateManifests();

        return alternates.FirstOrDefault(a => TryParseSwiftVersion(a.SwiftVersion, out var version) && version == requested).FileName
            ?? alternates.FirstOrDefault(a => TryParseSwiftVersion(a.ToolsVersion, out var version) && version == requested).FileName;
    }

    /// <summary>
    /// The version-specific manifests, which the registry API lists in the <c>Link</c> header of
    /// <c>Package.swift</c>: their file name, the Swift version in it, and the tools version they
    /// declare. Manifests without a tools version are left out.
    /// </summary>
    public IReadOnlyList<(string FileName, string SwiftVersion, string ToolsVersion)> GetAlternateManifests()
    {
        var alternates = new List<(string FileName, string SwiftVersion, string ToolsVersion)>();
        foreach (var (fileName, content) in Manifests.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            var match = VersionSpecificManifestRegex().Match(fileName);
            if (match.Success && GetToolsVersion(content) is { } toolsVersion)
            {
                alternates.Add((fileName, match.Groups[1].Value, toolsVersion));
            }
        }

        return alternates;
    }

    /// <summary>
    /// The tools version a manifest declares on its first line, e.g. <c>5.7</c> for
    /// <c>// swift-tools-version:5.7</c>.
    /// </summary>
    /// <returns>The version, or null if the manifest does not declare one.</returns>
    public static string GetToolsVersion(string manifest)
    {
        var firstLine = manifest?.Split('\n', 2)[0];
        var match = firstLine == null ? Match.Empty : ToolsVersionRegex().Match(firstLine);

        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Formats a time as the registry API writes it: ISO 8601 without fractions of a second, which
    /// is what Swift Package Manager's JSON decoder reads.
    /// </summary>
    public static string FormatTime(DateTime utc)
    {
        return utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses a Swift version into its three numbers, with missing ones as 0, so <c>5.7</c> and
    /// <c>5.7.0</c> are the same version.
    /// </summary>
    private static bool TryParseSwiftVersion(string text, out (int Major, int Minor, int Patch) version)
    {
        version = default;

        var parts = text?.Split('.');
        if (parts == null || parts.Length > 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = (numbers[0], numbers[1], numbers[2]);
        return true;
    }

    private static string GetString(JsonObject item, string property)
    {
        return GetText(item?[property]);
    }

    private static string GetText(JsonNode node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    [GeneratedRegex(@"\APackage@swift-(\d+(?:\.\d+){0,2})\.swift\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionSpecificManifestRegex();

    // Swift Package Manager's ToolsVersionParser takes any spacing and case around the label, and
    // ignores what follows a ';'.
    [GeneratedRegex(@"\A\s*//\s*swift-tools-version\s*:\s*(\d+(?:\.\d+){0,2})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ToolsVersionRegex();
}
