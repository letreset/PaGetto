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
/// Entra ID or another OpenID Connect provider.
/// </summary>
public partial class SingleSignOnSyncService
{
    private const string AdminRoleValue = "Admin";
    private const string DefaultEntraRoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    private const string DefaultOidcRoleClaim = "groups";

    private readonly IUserService _userService;
    private readonly IGroupService _groupService;
    private readonly IOptions<PaGettoOptions> _options;
    private readonly ILogger<SingleSignOnSyncService> _logger;

    public SingleSignOnSyncService(
        IUserService userService,
        IGroupService groupService,
        IOptions<PaGettoOptions> options,
        ILogger<SingleSignOnSyncService> logger)
    {
        _userService = userService;
        _groupService = groupService;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Provisions or updates a user from the provider's claims, syncs admin status and
    /// group memberships based on the roles in the token.
    /// Called from the OnTokenValidated OIDC event.
    /// </summary>
    public async Task OnTokenValidatedAsync(ClaimsPrincipal principal, AuthProvider provider, CancellationToken cancellationToken)
    {
        var externalId = provider == AuthProvider.Entra
            ? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
              ?? principal.FindFirstValue("oid")
            : principal.FindFirstValue("sub");

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

        // Upsert the user
        var user = await _userService.FindByExternalIdAsync(provider, externalId, cancellationToken);
        if (user == null)
        {
            if (await IsUsernameTakenAsync(username, null, cancellationToken))
            {
                LogUsernameTaken(provider, username);
                throw new UnauthorizedAccessException($"The username '{username}' is already used by another account.");
            }

            LogProvisioningUser(provider, username, externalId);
            user = await _userService.CreateExternalUserAsync(provider, externalId, username, displayName, email, cancellationToken);
        }
        else
        {
            var changed = false;
            if (user.DisplayName != displayName) { user.DisplayName = displayName; changed = true; }
            if (user.Username != username && !await IsUsernameTakenAsync(username, user.Id, cancellationToken))
            {
                user.Username = username;
                changed = true;
            }
            if (!string.IsNullOrEmpty(email) && user.Email != email) { user.Email = email; changed = true; }

            if (changed)
            {
                user.UpdatedAtUtc = DateTime.UtcNow;
                await _userService.UpdateUserAsync(user, cancellationToken);
            }
        }

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
        var roleClaim = provider == AuthProvider.Entra
            ? auth?.Entra?.RoleClaim ?? DefaultEntraRoleClaim
            : auth?.Oidc?.RoleClaim ?? DefaultOidcRoleClaim;
        var roles = principal.FindAll(roleClaim).Select(c => c.Value).ToList();

        // Bidirectional admin sync: grant or revoke based on token
        var hasAdminRole = roles.Contains(AdminRoleValue, StringComparer.OrdinalIgnoreCase);
        if (hasAdminRole != user.IsAdmin)
        {
            LogAdminRoleSync(hasAdminRole ? "Granting" : "Revoking", provider, user.Username);
            await _userService.SetAdminAsync(user.Id, hasAdminRole, cancellationToken);
        }

        // Sync group memberships from the roles (full bidirectional reconciliation)
        await _groupService.SyncAppRoleMembershipsAsync(user.Id, roles, cancellationToken);

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
        }
    }

    private async Task<bool> IsUsernameTakenAsync(string username, Guid? userId, CancellationToken cancellationToken)
    {
        var existing = await _userService.FindByUsernameAsync(username, cancellationToken);
        return existing != null && existing.Id != userId;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{AuthProvider} token is missing the user id claim")]
    private partial void LogMissingExternalId(AuthProvider authProvider);

    [LoggerMessage(Level = LogLevel.Information, Message = "Provisioning new {AuthProvider} user: {Username} (ID: {ExternalId})")]
    private partial void LogProvisioningUser(AuthProvider authProvider, string username, string externalId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login denied: the username {Username} of the new {AuthProvider} user is already used by another account")]
    private partial void LogUsernameTaken(AuthProvider authProvider, string username);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login denied: {AuthProvider} user {Username} is disabled")]
    private partial void LogLoginDeniedDisabled(AuthProvider authProvider, string username);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login denied: {AuthProvider} user {Username} does not have UI login permission")]
    private partial void LogLoginDeniedNoUIPermission(AuthProvider authProvider, string username);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Action} admin for {AuthProvider} user {Username} based on its roles")]
    private partial void LogAdminRoleSync(string action, AuthProvider authProvider, string username);
}
