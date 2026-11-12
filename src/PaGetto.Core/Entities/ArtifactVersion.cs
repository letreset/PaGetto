using System;
using System.Collections.Generic;

namespace PaGetto.Core.Entities;

/// <summary>
/// One version of an <see cref="Artifact"/>.
/// </summary>
public class ArtifactVersion
{
    public int Key { get; set; }

    public int ArtifactKey { get; set; }
    public Artifact Artifact { get; set; }

    /// <summary>
    /// The version as it was published.
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// The lowercased version. Unique within the artifact, and used in storage paths.
    /// </summary>
    public string NormalizedVersion { get; set; }

    public bool IsPrerelease { get; set; }

    /// <summary>
    /// False for a version that was withdrawn but is still downloadable (a retracted pub version).
    /// </summary>
    public bool Listed { get; set; }

    public string Description { get; set; }

    /// <summary>
    /// The version's manifest as JSON, in the shape its ecosystem's API returns it (the pubspec for pub).
    /// </summary>
    public string MetadataJson { get; set; }

    /// <summary>
    /// The lowercase hex SHA-256 of the archive. Null for a version made of several <see cref="Files"/>.
    /// </summary>
    public string ContentHash { get; set; }

    /// <summary>
    /// The length of the archive in bytes, or the total length of the <see cref="Files"/>.
    /// </summary>
    public long Size { get; set; }

    public long Downloads { get; set; }
    public bool HasReadme { get; set; }

    /// <summary>
    /// The upstream source the version was mirrored from, or null for a published version.
    /// </summary>
    public string CachedFrom { get; set; }

    public DateTime Published { get; set; }

    /// <summary>
    /// The files of the version, for ecosystems whose versions have several (Maven). Empty otherwise.
    /// </summary>
    public List<ArtifactFile> Files { get; set; }
}
