namespace PaGetto.Core.Cran;

/// <summary>
/// The result of <see cref="ICranPackageService.UploadAsync"/>.
/// </summary>
public sealed class CranUploadResult
{
    public CranUploadStatus Status { get; init; }

    /// <summary>
    /// The package name, as its <c>DESCRIPTION</c> file has it. Null when the package could not be read.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The version, as its <c>DESCRIPTION</c> file has it. Null when the package could not be read.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// Why the package was rejected, for the person uploading it. Null on success.
    /// </summary>
    public string Message { get; init; }
}

public enum CranUploadStatus
{
    /// <summary>
    /// The package is not a valid source package, or not the one its file name names.
    /// </summary>
    InvalidPackage,

    /// <summary>
    /// The package is larger than the feed allows.
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
