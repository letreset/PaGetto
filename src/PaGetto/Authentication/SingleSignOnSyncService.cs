using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaGetto.Authentication;

/// <summary>
/// Handles user provisioning and role-based group synchronization when a user signs in through
/// Entra ID, another OpenID Connect provider or a SAML identity provider.
/// </summary>
public partial class SingleSignOnSyncService
{
    private const string DefaultEntraRoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    private const string DefaultOidcRoleClaim = "groups";

    private readonly ExternalAccountService _accounts;
    private readonly IOptions<PaGettoOptions> _options;
    private readonly ILogger<SingleSignOnSyncService> _logger;

    public SingleSignOnSyncService(
        ExternalAccountService accounts,
        IOptions<PaGettoOptions> options,
        ILogger<SingleSignOnSyncService> logger)
    {
        _accounts = accounts;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Provisions or updates a user from the provider's claims, syncs admin status and
    /// group memberships based on the roles in the token or assertion.
    /// Called once the provider's response has been validated.
    /// </summary>
    public async Task OnTokenValidatedAsync(ClaimsPrincipal principal, AuthProvider provider, CancellationToken cancellationToken)
    {
        var externalId = provider switch
        {
            AuthProvider.Entra => principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
                                  ?? principal.FindFirstValue("oid"),
            // The NameID of the SAML assertion.
            AuthProvider.Saml => principal.FindFirstValue(ClaimTypes.NameIdentifier),
            _ => principal.FindFirstValue("sub"),
        };

        if (string.IsNullOrEmpty(externalId))
        {
            LogMissingExternalId(provider);
            return;
        }

        // Only a verified mail or email claim (or UPN) is stored as the mailbox, since User.Email now drives
        // notification delivery. preferred_username is deliberately not used: it is often a UPN that
        // is not a deliverable address, so notifications sent to it would bounce.
        var email = principal.FindFirstValue(ClaimTypes.Email)
                    ?? principal.FindFirstValue("mail")
                    ?? principal.FindFirstValue("email")
                    ?? principal.FindFirstValue(ClaimTypes.Upn)
                    ?? principal.FindFirstValue("upn");

        // Providers other than Entra ID name their users with preferred_username (the Keycloak,
        // GitLab or Okta login name), which beats an opaque subject id as the username.
        var username = email
                       ?? (provider == AuthProvider.Oidc ? principal.FindFirstValue("preferred_username") : null)
                       ?? externalId;
        var displayName = principal.FindFirstValue(ClaimTypes.GivenName) is { } given
                          && principal.FindFirstValue(ClaimTypes.Surname) is { } surname
            ? $"{given} {surname}"
            : principal.FindFirstValue("name") ?? username;

        var user = await _accounts.UpsertAsync(provider, externalId, username, displayName, email, cancellationToken);

        if (!user.IsEnabled)
        {
            LogLoginDeniedDisabled(provider, user.Username);
            throw new UnauthorizedAccessException($"Account '{user.Username}' has been disabled.");
        }

        if (!user.CanLoginToUI)
        {
            LogLoginDeniedNoUIPermission(provider, user.Username);
            throw new UnauthorizedAccessException($"Account '{user.Username}' is not permitted to sign in to the web UI.");
        }

        // Read roles claim from the token
        var auth = _options.Value.Authentication;
        var roleClaim = provider switch
        {
            AuthProvider.Entra => auth?.Entra?.RoleClaim ?? DefaultEntraRoleClaim,
            AuthProvider.Saml => auth?.Saml?.RoleAttribute ?? DefaultOidcRoleClaim,
            _ => auth?.Oidc?.RoleClaim ?? DefaultOidcRoleClaim,
        };
        var roles = principal.FindAll(roleClaim).Select(c => c.Value).ToList();

        // Bidirectional admin and group sync (full reconciliation with the token)
        await _accounts.SyncRolesAsync(user, roles, cancellationToken);

        // Add PaGetto-specific claims to the principal
        var identity = principal.Identity as ClaimsIdentity;
        if (identity != null)
        {
            var existing = identity.FindAll(ClaimTypes.NameIdentifier).ToList();
            foreach (var c in existing)
                identity.RemoveClaim(c);

            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
            identity.AddClaim(new Claim("auth_provider", provider.ToString()));
            identity.AddClaim(new Claim(AuthenticationConstants.IsAdminClaim, user.IsAdmin ? "true" : "false"));

            // SAML assertions often carry no name, which the web UI shows.
            if (identity.Name == null)
                identity.AddClaim(new Claim(identity.NameClaimType, user.Username));
            if (identity.FindFirst("DisplayName") == null && identity.FindFirst("name") == null)
                identity.AddClaim(new Claim("DisplayName", user.DisplayName ?? user.Username));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{AuthProvider} token is missing the user id claim")]
    private partial void LogMissingExternalId(AuthProvider authProvider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login denied: {AuthProvider} user {Username} is disabled")]
    private partial void LogLoginDeniedDisabled(AuthProvider authProvider, string username);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login denied: {AuthProvider} user {Username} does not have UI login permission")]
    private partial void LogLoginDeniedNoUIPermission(AuthProvider authProvider, string username);
}
