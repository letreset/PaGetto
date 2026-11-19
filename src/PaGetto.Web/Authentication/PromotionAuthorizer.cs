using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;

namespace PaGetto.Web.Authentication;

/// <summary>
/// Decides whether a version may be promoted from one feed to another: the pull permission on the
/// source feed, the push permission on the target feed, and for a move the delete permission on
/// the source feed, then neither feed may be read-only. In the <c>Legacy</c> mode the API key
/// stands for every permission. Scoped.
/// </summary>
public class PromotionAuthorizer
{
    private readonly IFeedService _feeds;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IAuthenticationService _authentication;
    private readonly IPermissionService _permissionService;
    private readonly IOptionsSnapshot<PaGettoOptions> _options;

    public PromotionAuthorizer(
        IFeedService feeds,
        IFeedSettingsResolver feedSettings,
        IAuthenticationService authentication,
        IPermissionService permissionService,
        IOptionsSnapshot<PaGettoOptions> options)
    {
        _feeds = feeds ?? throw new ArgumentNullException(nameof(feeds));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <param name="user">The caller.</param>
    /// <param name="apiKey">The API key the caller sent, or null. Only the <c>Legacy</c> mode checks it.</param>
    /// <param name="source">The feed that has the version.</param>
    /// <param name="target">The feed to promote it to.</param>
    /// <param name="move">Whether the version is removed from <paramref name="source"/>.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    public async Task<FeedChangeAccess> CheckAsync(ClaimsPrincipal user, string apiKey, Feed source, Feed target, bool move, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        bool authorized;
        if (IsLegacyMode)
        {
            authorized = await _authentication.AuthenticateAsync(apiKey, cancellationToken);
        }
        else
        {
            authorized = TryGetUserId(user, out var userId)
                && await _permissionService.CanPullAsync(userId, source.Id, cancellationToken)
                && await _permissionService.CanPushAsync(userId, target.Id, cancellationToken)
                && (!move || await _permissionService.CanDeleteAsync(userId, source.Id, cancellationToken));
        }

        if (!authorized)
        {
            return FeedChangeAccess.Unauthorized;
        }

        return _feedSettings.GetIsReadOnlyMode(target) || (move && _feedSettings.GetIsReadOnlyMode(source))
            ? FeedChangeAccess.ReadOnly
            : FeedChangeAccess.Allowed;
    }

    /// <summary>
    /// The feeds a signed-in user may promote a version of <paramref name="source"/> to: the other
    /// feeds of its type that the user may push to and that are not read-only. Empty in the
    /// <c>Legacy</c> mode, which has no signed-in users.
    /// </summary>
    public async Task<IReadOnlyList<Feed>> GetTargetsAsync(ClaimsPrincipal user, Feed source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(source);

        if (IsLegacyMode || !TryGetUserId(user, out var userId))
        {
            return [];
        }

        var targets = new List<Feed>();
        foreach (var feed in (await _feeds.GetAllFeedsAsync(cancellationToken)).Where(f => f.Type == source.Type && f.Id != source.Id))
        {
            if (!_feedSettings.GetIsReadOnlyMode(feed) && await _permissionService.CanPushAsync(userId, feed.Id, cancellationToken))
            {
                targets.Add(feed);
            }
        }

        return targets;
    }

    private bool IsLegacyMode => (_options.Value.Authentication?.Mode ?? AuthenticationMode.Legacy) == AuthenticationMode.Legacy;

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        userId = Guid.Empty;
        return Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out userId);
    }
}
