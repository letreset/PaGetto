namespace PaGetto.Web.Authentication;

/// <summary>
/// Whether the caller may change the current feed, see <see cref="FeedChangeAuthorizer"/>.
/// </summary>
public enum FeedChangeAccess
{
    Allowed,

    /// <summary>
    /// The caller has no valid API key, or lacks the permission.
    /// </summary>
    Unauthorized,

    /// <summary>
    /// The caller may change the feed, but the feed is read-only.
    /// </summary>
    ReadOnly,
}
