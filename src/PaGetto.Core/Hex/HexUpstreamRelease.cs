using PaGetto.Core.Configuration;

namespace PaGetto.Core.Hex;

/// <summary>
/// A version of a Hex package as the registry of an upstream repository lists it.
/// </summary>
public sealed class HexUpstreamRelease
{
    public HexRelease Release { get; init; }

    /// <summary>
    /// The mirror that listed the version.
    /// </summary>
    public MirrorOptions Mirror { get; init; }
}
