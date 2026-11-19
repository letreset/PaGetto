namespace PaGetto.Core.Quarantine;

/// <summary>
/// The outcome of <see cref="PromotionService.PromoteAsync"/>.
/// </summary>
public enum PromotionResult
{
    /// <summary>
    /// The version was copied, or moved.
    /// </summary>
    Success,

    /// <summary>
    /// The source feed has no such version, or it is quarantined.
    /// </summary>
    NotFound,

    /// <summary>
    /// The target feed already has the version.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// The target is the source feed, or a feed of another type.
    /// </summary>
    InvalidTarget,

    /// <summary>
    /// The target feed refused the stored package.
    /// </summary>
    Invalid,
}
