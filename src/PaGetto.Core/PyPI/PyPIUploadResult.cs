namespace PaGetto.Core.PyPI;

/// <summary>
/// The outcome of <see cref="IPyPIPackageService.UploadAsync"/>.
/// </summary>
public sealed class PyPIUploadResult
{
    public PyPIUploadStatus Status { get; init; }

    /// <summary>
    /// The normalized project name, or null if the upload had no valid one.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The normalized version, or null if the upload had no valid one.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// Why the upload was rejected, shown to the person uploading. Null on success.
    /// </summary>
    public string Message { get; init; }
}

public enum PyPIUploadStatus
{
    /// <summary>
    /// The file is stored, or the same file was stored already.
    /// </summary>
    Success,

    /// <summary>
    /// The version has a file of that name with other content, and the feed doesn't allow overwriting it.
    /// </summary>
    Conflict,

    /// <summary>
    /// The file is larger than the feed allows.
    /// </summary>
    TooLarge,

    /// <summary>
    /// The upload is not a valid distribution of the project.
    /// </summary>
    Invalid,
}
