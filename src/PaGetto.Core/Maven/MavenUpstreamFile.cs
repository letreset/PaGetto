using System;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;

namespace PaGetto.Core.Maven;

/// <summary>
/// A file downloaded from a mirror by <see cref="MavenUpstreamClient.DownloadFileOrNullAsync"/>.
/// </summary>
public sealed class MavenUpstreamFile : IDisposable
{
    public ArtifactFileContent Content { get; init; }

    /// <summary>
    /// The mirror the file came from.
    /// </summary>
    public MirrorOptions Mirror { get; init; }

    /// <summary>
    /// When the mirror says the file last changed, or null.
    /// </summary>
    public DateTime? LastModified { get; init; }

    public void Dispose()
    {
        Content?.Dispose();
    }
}
