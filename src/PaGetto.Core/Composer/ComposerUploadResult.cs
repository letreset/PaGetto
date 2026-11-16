namespace PaGetto.Core.Composer;

/// <summary>
/// The result of <see cref="IComposerPackageService.UploadAsync"/>.
/// </summary>
public sealed class ComposerUploadResult
{
    public ComposerUploadStatus Status { get; init; }

    /// <summary>
    /// Why the zip was rejected, for the person uploading it. Null on success.
    /// </summary>
    public string Message { get; init; }
}

public enum ComposerUploadStatus
{
    /// <summary>
    /// The name, the version or the zip is not valid.
    /// </summary>
    InvalidPackage,

    /// <summary>
    /// The zip is larger than the feed allows.
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
