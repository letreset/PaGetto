using System;

namespace PaGetto.Core.Authentication;

/// <summary>
/// What a new personal access token may do, within what its user may do. Every token can read.
/// </summary>
/// <param name="CanPush">Whether it may publish.</param>
/// <param name="CanDelete">Whether it may unlist and delete packages.</param>
/// <param name="FeedId">The only feed it may be used with, or null for every feed.</param>
public record TokenPermissions(bool CanPush, bool CanDelete, Guid? FeedId)
{
    /// <summary>
    /// Everything its user may do, in every feed.
    /// </summary>
    public static TokenPermissions All { get; } = new(CanPush: true, CanDelete: true, FeedId: null);

    /// <summary>
    /// Reading only, in every feed.
    /// </summary>
    public static TokenPermissions ReadOnly { get; } = new(CanPush: false, CanDelete: false, FeedId: null);
}
