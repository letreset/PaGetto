namespace PaGetto.Core.Cargo;

/// <summary>
/// The result of <see cref="ICargoPackageService.PublishAsync"/>.
/// </summary>
public sealed class CargoPublishResult
{
    public CargoPublishStatus Status { get; init; }

    /// <summary>
    /// The published crate's name. Null when the metadata has no valid name.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The published crate's version. Null when the metadata has no valid version.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// Why the crate was rejected, for the person publishing it. Null on success.
    /// </summary>
    public string Message { get; init; }
}

public enum CargoPublishStatus
{
    /// <summary>
    /// The request or the crate is not valid.
    /// </summary>
    InvalidPackage,

    /// <summary>
    /// The feed already has this version and does not allow overwriting it.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// The crate was published.
    /// </summary>
    Success,
}
