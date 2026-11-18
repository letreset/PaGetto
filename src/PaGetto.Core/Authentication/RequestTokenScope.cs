using System;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Authentication;

/// <summary>
/// The personal access token the current request signed in with. A token can be limited to
/// reading, or to one feed, so what its user may do in this request is what the user may do and
/// the token allows. Scoped: one per request, empty for a request without a token.
/// </summary>
public class RequestTokenScope
{
    /// <summary>
    /// The token, or null if the request did not sign in with one.
    /// </summary>
    public PersonalAccessToken Token { get; private set; }

    /// <summary>
    /// Records the token the request signed in with.
    /// </summary>
    public void Use(PersonalAccessToken token)
    {
        Token = token;
    }

    /// <summary>
    /// Whether the token allows reading <paramref name="feedId"/> as <paramref name="userId"/>.
    /// True when the request has no token of that user.
    /// </summary>
    public bool AllowsPull(Guid userId, Guid feedId)
    {
        return !Limits(userId) || AllowsFeed(feedId);
    }

    /// <summary>
    /// Whether the token allows publishing to <paramref name="feedId"/> as <paramref name="userId"/>.
    /// </summary>
    public bool AllowsPush(Guid userId, Guid feedId)
    {
        return !Limits(userId) || (Token.CanPush && AllowsFeed(feedId));
    }

    /// <summary>
    /// Whether the token allows unlisting and deleting in <paramref name="feedId"/> as <paramref name="userId"/>.
    /// </summary>
    public bool AllowsDelete(Guid userId, Guid feedId)
    {
        return !Limits(userId) || (Token.CanDelete && AllowsFeed(feedId));
    }

    private bool Limits(Guid userId)
    {
        return Token != null && Token.UserId == userId;
    }

    private bool AllowsFeed(Guid feedId)
    {
        return Token.FeedId == null || Token.FeedId == feedId;
    }
}
