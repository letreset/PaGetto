namespace PaGetto.Core.Swift;

/// <summary>
/// The result of <see cref="ISwiftPackageService.PublishAsync"/>.
/// </summary>
public sealed class SwiftPublishResult
{
    public SwiftPublishStatus Status { get; init; }

    /// <summary>
    /// Why the release was rejected, for the person publishing it. Null on success.
    /// </summary>
    public string Message { get; init; }
}

public enum SwiftPublishStatus
{
    /// <summary>
    /// The identifier or the version is not valid.
    /// </summary>
    InvalidRequest,

    /// <summary>
    /// The source archive or the metadata is not valid.
    /// </summary>
    InvalidPackage,

    /// <summary>
    /// The source archive is larger than the feed allows.
    /// </summary>
    TooLarge,

    /// <summary>
    /// The feed already has this version and does not allow overwriting it.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// The release was published.
    /// </summary>
    Success,
}
