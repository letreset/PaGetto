namespace PaGetto.Core.Go;

/// <summary>
/// The result of <see cref="IGoModuleService.UploadAsync"/>.
/// </summary>
public sealed class GoUploadResult
{
    public GoUploadStatus Status { get; init; }

    /// <summary>
    /// Why the zip was rejected, for the person uploading it. Null on success.
    /// </summary>
    public string Message { get; init; }
}

public enum GoUploadStatus
{
    /// <summary>
    /// The path, the version or the zip is not valid.
    /// </summary>
    InvalidModule,

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
