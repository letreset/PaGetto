using PaGetto.Core.Configuration;

namespace PaGetto.Core.VsCode;

/// <summary>
/// An extension that a mirror of a VS Code feed has, as the mirror's gallery answers it.
/// </summary>
public sealed class VsCodeUpstreamExtension
{
    public VsCodeGalleryExtension Extension { get; init; }

    public MirrorOptions Mirror { get; init; }
}
