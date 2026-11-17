using PaGetto.Core.Configuration;

namespace PaGetto.Core.Swift;

/// <summary>
/// A release of a package on a mirror of a Swift feed.
/// </summary>
public sealed class SwiftUpstreamRelease
{
    /// <summary>
    /// The version, as the mirror lists it.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// The mirror that lists the release.
    /// </summary>
    public MirrorOptions Mirror { get; init; }
}
