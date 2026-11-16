namespace PaGetto.Core.Hex;

/// <summary>
/// The result of <see cref="IHexPackageService.PublishAsync"/>.
/// </summary>
public sealed class HexPublishResult
{
    public HexPublishStatus Status { get; init; }

    /// <summary>
    /// The published package's name. Null when the tarball has no valid name.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The published package's version. Null when the tarball has no valid version.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// Why the package was rejected, for the person publishing it. Null on success.
    /// </summary>
    public string Message { get; init; }
}

public enum HexPublishStatus
{
    /// <summary>
    /// The tarball is not a valid package.
    /// </summary>
    InvalidPackage,

    /// <summary>
    /// The feed already has this version, and it may not be replaced or the client did not ask to.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// The tarball is larger than the feed allows.
    /// </summary>
    TooLarge,

    /// <summary>
    /// The package was published.
    /// </summary>
    Success,
}
