using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Feeds;

namespace PaGetto.Web.Authentication;

/// <summary>
/// Decides whether the caller of a package API may change the current feed: publish to it, or
/// unlist and delete its packages. Each API answers a refusal in its own client's format.
/// </summary>
public class FeedChangeAuthorizer
{
    private readonly IFeedContext _feedContext;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IAuthenticationService _authentication;
    private readonly IPermissionService _permissionService;
    private readonly IOptionsSnapshot<PaGettoOptions> _options;

    public FeedChangeAuthorizer(
        IFeedContext feedContext,
        IFeedSettingsResolver feedSettings,
        IAuthenticationService authentication,
        IPermissionService permissionService,
        IOptionsSnapshot<PaGettoOptions> options)
    {
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <param name="user">The caller.</param>
    /// <param name="apiKey">
    /// The API key the caller sent, as its client sends it. Only the <c>Legacy</c> authentication
    /// mode checks it; the other modes check the permission of the signed-in user.
    /// </param>
    /// <param name="permission">
    /// <see cref="FeedPermissionRequirement.Push"/> or <see cref="FeedPermissionRequirement.Delete"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<FeedChangeAccess> CheckAsync(
        ClaimsPrincipal user,
        string apiKey,
        string permission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var feed = _feedContext.CurrentFeed;
        var authMode = _options.Value.Authentication?.Mode ?? AuthenticationMode.Legacy;

        bool authorized;
        if (authMode == AuthenticationMode.Legacy)
        {
            // Static auth mode: the configured API key.
            authorized = await _authentication.AuthenticateAsync(apiKey, cancellationToken);
        }
        else
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            authorized = Guid.TryParse(userIdClaim, out var userId)
                && (permission == FeedPermissionRequirement.Push
                    ? await _permissionService.CanPushAsync(userId, feed.Id, cancellationToken)
                    : await _permissionService.CanDeleteAsync(userId, feed.Id, cancellationToken));
        }

        if (!authorized)
        {
            return FeedChangeAccess.Unauthorized;
        }

        return _feedSettings.GetIsReadOnlyMode(feed) ? FeedChangeAccess.ReadOnly : FeedChangeAccess.Allowed;
    }
}
