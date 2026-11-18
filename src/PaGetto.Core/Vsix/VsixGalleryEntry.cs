using System;

namespace PaGetto.Core.Vsix;

/// <summary>
/// An extension in a gallery feed: the version Visual Studio offers, and where it gets the file
/// and the images from.
/// </summary>
public sealed class VsixGalleryEntry
{
    public VsixManifest Manifest { get; init; }

    public DateTime Published { get; init; }

    public DateTime Updated { get; init; }

    /// <summary>
    /// How many times the extension was downloaded, or null if unknown.
    /// </summary>
    public long? Downloads { get; init; }

    /// <summary>
    /// The absolute URL of the VSIX file.
    /// </summary>
    public string DownloadUrl { get; init; }

    /// <summary>
    /// The absolute URL of the icon, or null.
    /// </summary>
    public string IconUrl { get; init; }

    /// <summary>
    /// The absolute URL of the preview image, or null.
    /// </summary>
    public string PreviewImageUrl { get; init; }
}
