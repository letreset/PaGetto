using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaGetto.Core.VsCode;

/// <summary>
/// The manifest of a version of a VS Code extension, stored as its
/// <see cref="Entities.ArtifactVersion.MetadataJson"/>: the manifest of each of its VSIX files,
/// one per platform. The files themselves are the version's <see cref="Entities.ArtifactFile"/>s.
/// </summary>
public sealed class VsCodeVersionManifest
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public List<VsCodeManifest> Files { get; init; } = [];

    /// <summary>
    /// The manifest that describes the version: the universal file's, or else the first platform's.
    /// </summary>
    [JsonIgnore]
    public VsCodeManifest Main => Find(VsCodeTargetPlatform.Universal) ?? Files.OrderBy(f => f.TargetPlatform, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>
    /// The manifest of the file of a platform, or null if the version has none.
    /// </summary>
    public VsCodeManifest Find(string targetPlatform)
    {
        return Files.Find(f => f.TargetPlatform == targetPlatform);
    }

    /// <summary>
    /// Adds the manifest of a file, or replaces the manifest of the file of the same platform.
    /// </summary>
    public void AddOrReplace(VsCodeManifest manifest)
    {
        Files.RemoveAll(f => f.TargetPlatform == manifest.TargetPlatform);
        Files.Add(manifest);
        Files.Sort((a, b) => string.CompareOrdinal(a.TargetPlatform, b.TargetPlatform));
    }

    public static VsCodeVersionManifest Parse(string json)
    {
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                return JsonSerializer.Deserialize<VsCodeVersionManifest>(json, _jsonOptions) ?? new VsCodeVersionManifest();
            }
            catch (JsonException)
            {
            }
        }

        return new VsCodeVersionManifest();
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, _jsonOptions);
    }
}
