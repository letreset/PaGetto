using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace PaGetto.Core.VsCode;

/// <summary>
/// The manifest of one VSIX file of a VS Code extension, for one platform: the parts of its
/// <c>extension.vsixmanifest</c> that the gallery API and the web UI use. A version keeps the
/// manifest of each of its files in a <see cref="VsCodeVersionManifest"/>.
/// </summary>
public sealed class VsCodeManifest
{
    /// <summary>
    /// The publisher, as published.
    /// </summary>
    public string Publisher { get; init; }

    /// <summary>
    /// The extension's name without the publisher, as published.
    /// </summary>
    public string Name { get; init; }

    public string Version { get; init; }

    /// <summary>
    /// The platform of the file (see <see cref="VsCodeTargetPlatform"/>).
    /// </summary>
    public string TargetPlatform { get; init; }

    public string DisplayName { get; init; }

    public string Description { get; init; }

    public List<string> Tags { get; init; } = [];

    public List<string> Categories { get; init; } = [];

    /// <summary>
    /// The gallery flags, such as <c>Public Preview</c>.
    /// </summary>
    public string GalleryFlags { get; init; }

    /// <summary>
    /// The properties of the manifest, which the gallery API lists with the version.
    /// </summary>
    public List<VsCodeGalleryProperty> Properties { get; init; } = [];

    /// <summary>
    /// The paths in the file of its assets, by asset type, such as the <c>package.json</c> of
    /// <see cref="VsCodeGalleryApi.ManifestAsset"/>.
    /// </summary>
    public Dictionary<string, string> Assets { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The name of the file, as stored and downloaded.
    /// </summary>
    public string FileName { get; set; }

    /// <summary>
    /// The extension's identifier, <c>publisher.name</c>, as published.
    /// </summary>
    [JsonIgnore]
    public string Id => $"{Publisher}.{Name}";

    /// <summary>
    /// Whether the file is a pre-release, which its manifest's properties say (<see cref="VsCodeGalleryApi.PreReleaseProperty"/>).
    /// </summary>
    [JsonIgnore]
    public bool IsPreRelease => string.Equals(GetProperty(VsCodeGalleryApi.PreReleaseProperty), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The value of a property, or null if the manifest doesn't have it or it is empty.
    /// </summary>
    public string GetProperty(string key)
    {
        var value = Properties?.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// The name of the VSIX file of an extension, as VS Code's packaging tool names it, with the platform unless it is universal.
    /// </summary>
    public static string GetFileName(string id, string version, string targetPlatform)
    {
        return targetPlatform == VsCodeTargetPlatform.Universal
            ? $"{id}-{version}.vsix"
            : $"{id}-{version}@{targetPlatform}.vsix";
    }
}
