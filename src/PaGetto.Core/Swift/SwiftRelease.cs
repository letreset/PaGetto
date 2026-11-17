using PaGetto.Core.Entities;

namespace PaGetto.Core.Swift;

/// <summary>
/// A release stored in a Swift feed.
/// </summary>
public sealed class SwiftRelease
{
    /// <summary>
    /// The stored version. Its <see cref="ArtifactVersion.ContentHash"/> is the SHA-256 of the source
    /// archive, which the registry API calls its checksum.
    /// </summary>
    public ArtifactVersion Version { get; init; }

    /// <summary>
    /// The release's manifest (see <see cref="ArtifactVersion.MetadataJson"/>).
    /// </summary>
    public SwiftManifest Manifest { get; init; }
}
