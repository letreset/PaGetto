using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// The manifest of a gem version, stored as its JSON: the gemspec of each of its gem files, one per
/// platform, each with the file's SHA-256. The gemspecs of a version can differ by platform, e.g. in
/// their dependencies.
/// </summary>
public sealed class RubyGemsManifest
{
    private static readonly JsonSerializerOptions _options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The gemspecs, the <c>ruby</c> platform first, then the others in ordinal order.
    /// </summary>
    [JsonPropertyName("gems")]
    public List<RubyGemsSpecification> Gems { get; set; } = [];

    /// <summary>
    /// The gem name as published, from the first gemspec.
    /// </summary>
    [JsonIgnore]
    public string Name => Gems.FirstOrDefault()?.Name;

    /// <summary>
    /// The gemspec shown for the version: that of the <c>ruby</c> platform, or else the first.
    /// </summary>
    [JsonIgnore]
    public RubyGemsSpecification Main => Gems.FirstOrDefault(g => RubyGemsFileName.IsRubyPlatform(g.Platform)) ?? Gems.FirstOrDefault();

    /// <summary>
    /// Reads a stored manifest. Text that is not one gives an empty manifest.
    /// </summary>
    public static RubyGemsManifest Parse(string json)
    {
        try
        {
            var manifest = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<RubyGemsManifest>(json, _options);
            if (manifest?.Gems != null)
            {
                manifest.Gems.RemoveAll(g => g == null || string.IsNullOrEmpty(g.Version));
                return manifest;
            }
        }
        catch (JsonException)
        {
        }

        return new RubyGemsManifest();
    }

    /// <summary>
    /// Finds the gemspec of a platform.
    /// </summary>
    /// <returns>The gemspec, or null if the version has no gem of that platform.</returns>
    public RubyGemsSpecification Find(string platform)
    {
        return Gems.FirstOrDefault(g => SamePlatform(g.Platform, platform));
    }

    /// <summary>
    /// Adds the gemspec of a platform, replacing the one the version has for it.
    /// </summary>
    public void AddOrReplace(RubyGemsSpecification specification)
    {
        Gems.RemoveAll(g => SamePlatform(g.Platform, specification.Platform));
        Gems.Add(specification);
        Gems = Gems
            .OrderBy(g => RubyGemsFileName.IsRubyPlatform(g.Platform) ? 0 : 1)
            .ThenBy(g => g.Platform, StringComparer.Ordinal)
            .ToList();
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, _options);
    }

    private static bool SamePlatform(string left, string right)
    {
        return RubyGemsFileName.IsRubyPlatform(left)
            ? RubyGemsFileName.IsRubyPlatform(right)
            : string.Equals(left, right, StringComparison.Ordinal);
    }
}
