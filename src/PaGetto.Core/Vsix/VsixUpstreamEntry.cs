using PaGetto.Core.Configuration;

namespace PaGetto.Core.Vsix;

/// <summary>
/// An extension in the gallery feed of a mirror of a VSIX feed. A gallery lists one version of
/// each extension, so this is the only version the mirror offers.
/// </summary>
public sealed class VsixUpstreamEntry
{
    /// <summary>
    /// The entry, with its URLs on the mirror.
    /// </summary>
    public VsixGalleryEntry Entry { get; init; }

    public VsixVersion Version { get; init; }

    /// <summary>
    /// The mirror that has the extension.
    /// </summary>
    public MirrorOptions Mirror { get; init; }
}
