namespace PaGetto.Core.Pub;

/// <summary>
/// The result of <see cref="IPubPackageService.PublishAsync"/>.
/// </summary>
public sealed class PubPublishResult
{
    public PubPublishStatus Status { get; init; }

    /// <summary>
    /// The published package's name. Null when the archive could not be read.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The published package's version. Null when the archive could not be read.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// Why the package was rejected, for the person publishing it. Null on success.
    /// </summary>
    public string Message { get; init; }
}

public enum PubPublishStatus
{
    /// <summary>
    /// The archive is not a valid pub package.
    /// </summary>
    InvalidPackage,

    /// <summary>
    /// The feed already has this version and does not allow overwriting it.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// The package was published.
    /// </summary>
    Success,
}
