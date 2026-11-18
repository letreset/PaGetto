namespace PaGetto.Core.Vsix;

/// <summary>
/// The outcome of <see cref="IVsixPackageService.UploadAsync"/>.
/// </summary>
public enum VsixUploadStatus
{
    /// <summary>
    /// The file is not a valid VSIX file of a Visual Studio extension.
    /// </summary>
    InvalidPackage,

    /// <summary>
    /// The file is larger than the feed allows.
    /// </summary>
    TooLarge,

    /// <summary>
    /// The feed already has this version and does not allow overwriting it.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// The version was uploaded.
    /// </summary>
    Success,
}
