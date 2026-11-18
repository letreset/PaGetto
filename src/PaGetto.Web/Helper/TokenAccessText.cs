using System;
using System.Collections.Generic;
using PaGetto.Core.Entities;

namespace PaGetto.Web.Helper;

/// <summary>
/// What the web UI says a personal access token may do.
/// </summary>
public static class TokenAccessText
{
    /// <summary>
    /// E.g. "Read only", "Read and publish" or "Read, publish and delete", followed by the feed
    /// the token is limited to.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="feedNames">The names of the feeds by id.</param>
    public static string Describe(PersonalAccessToken token, IReadOnlyDictionary<Guid, string> feedNames)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(feedNames);

        var access = (token.CanPush, token.CanDelete) switch
        {
            (true, true) => "Read, publish and delete",
            (true, false) => "Read and publish",
            (false, true) => "Read and delete",
            _ => "Read only",
        };

        if (token.FeedId is not { } feedId)
        {
            return access + " · all feeds";
        }

        return feedNames.TryGetValue(feedId, out var name)
            ? $"{access} · {name}"
            : $"{access} · a deleted feed";
    }
}
