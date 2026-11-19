namespace PaGetto.Core.VsCode;

/// <summary>
/// The outcome of <see cref="IVsCodePackageService.UploadAsync"/>.
/// </summary>
public enum VsCodeUploadStatus
{
    /// <summary>
    /// The file is not a valid VSIX file of a VS Code extension.
    /// </summary>
    InvalidPackage,

    /// <summary>
    /// The file is larger than the feed allows.
    /// </summary>
    TooLarge,

    /// <summary>
    /// The feed already has this version for this platform and does not allow overwriting it.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// The file was uploaded.
    /// </summary>
    Success,
}
