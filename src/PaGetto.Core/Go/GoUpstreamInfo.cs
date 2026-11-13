using System;
using PaGetto.Core.Configuration;

namespace PaGetto.Core.Go;

/// <summary>
/// The <c>.info</c> of a version on a mirror of a Go feed.
/// </summary>
public sealed class GoUpstreamInfo
{
    /// <summary>
    /// The canonical version.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// The version's time, or null if the mirror gives none.
    /// </summary>
    public DateTime? Time { get; init; }

    /// <summary>
    /// The mirror that has the version.
    /// </summary>
    public MirrorOptions Mirror { get; init; }
}
