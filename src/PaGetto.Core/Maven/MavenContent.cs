using System.IO;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Maven;

/// <summary>
/// What <see cref="IMavenRepositoryService.GetAsync"/> serves for a path.
/// </summary>
public sealed class MavenContent
{
    /// <summary>
    /// The content. The caller disposes it.
    /// </summary>
    public Stream Stream { get; init; }

    public string ContentType { get; init; }

    /// <summary>
    /// The stored file, when the content is one; null for metadata and checksums.
    /// </summary>
    public ArtifactFile File { get; init; }
}
