using System.IO;

namespace PaGetto.Core.VsCode;

/// <summary>
/// A file of a version that the gallery serves, such as its VSIX file or its readme.
/// </summary>
public sealed class VsCodeAsset
{
    public Stream Content { get; init; }

    public string ContentType { get; init; }

    /// <summary>
    /// The name to download the file as, or null.
    /// </summary>
    public string FileName { get; init; }
}
