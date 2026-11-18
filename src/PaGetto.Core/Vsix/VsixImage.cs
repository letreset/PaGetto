namespace PaGetto.Core.Vsix;

/// <summary>
/// An image of an extension that the gallery feed links to: its icon or its preview image.
/// </summary>
public sealed class VsixImage
{
    public byte[] Content { get; init; }

    /// <summary>
    /// The media type, e.g. <c>image/png</c>.
    /// </summary>
    public string ContentType { get; init; }
}
