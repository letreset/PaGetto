namespace PaGetto.Core.CocoaPods;

/// <summary>
/// The result of <see cref="ICocoaPodsPackageService.PublishAsync"/>.
/// </summary>
public sealed class CocoaPodsPublishResult
{
    public CocoaPodsPublishStatus Status { get; init; }

    /// <summary>
    /// Why the pod was rejected, for the person publishing it. Null on success.
    /// </summary>
    public string Message { get; init; }

    /// <summary>
    /// The pod name of the podspec, or null if the podspec could not be read.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The version of the podspec, or null if the podspec could not be read.
    /// </summary>
    public string Version { get; init; }
}

public enum CocoaPodsPublishStatus
{
    /// <summary>
    /// The podspec or the source archive is not valid.
    /// </summary>
    InvalidPackage,

    /// <summary>
    /// The podspec or the source archive is larger than the feed allows.
    /// </summary>
    TooLarge,

    /// <summary>
    /// The feed already has this version and does not allow overwriting it.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// The version was published.
    /// </summary>
    Success,
}
