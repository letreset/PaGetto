using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using Microsoft.Extensions.Logging;

namespace PaGetto.Core.Authentication;

/// <summary>
/// Creates and updates the accounts of users who sign in through an external provider (Entra ID,
/// OpenID Connect, SAML or LDAP), and syncs their Admin flag and role-linked groups from the
/// provider's groups or roles.
/// </summary>
public partial class ExternalAccountService
{
    /// <summary>
    /// The role or group value that makes a user an administrator.
    /// </summary>
    public const string AdminRoleValue = "Admin";

    private readonly IUserService _userService;
    private readonly IGroupService _groupService;
    private readonly ILogger<ExternalAccountService> _logger;

    public ExternalAccountService(
        IUserService userService,
        IGroupService groupService,
        ILogger<ExternalAccountService> logger)
    {
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _groupService = groupService ?? throw new ArgumentNullException(nameof(groupService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Finds the user by its id at the provider and updates its name, display name and email, or
    /// creates it. An existing user keeps its username when the new one belongs to another account.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">
    /// The user is new and its username belongs to another account, which it must not take over.
    /// </exception>
    public async Task<User> UpsertAsync(
        AuthProvider provider,
        string externalId,
        string username,
        string displayName,
        string email,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByExternalIdAsync(provider, externalId, cancellationToken);
        if (user == null)
        {
            if (await IsUsernameTakenAsync(username, null, cancellationToken))
            {
                LogUsernameTaken(provider, username);
                throw new UnauthorizedAccessException($"The username '{username}' is already used by another account.");
            }

            LogProvisioningUser(provider, username, externalId);
            return await _userService.CreateExternalUserAsync(provider, externalId, username, displayName, email, cancellationToken);
        }

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

        return user;
    }

    /// <summary>
    /// Grants or revokes Admin by whether <paramref name="roles"/> has <see cref="AdminRoleValue"/>,
    /// and makes the user a member of exactly the role-linked groups whose role value is in
    /// <paramref name="roles"/>. Manually managed groups are left alone.
    /// </summary>
    public async Task SyncRolesAsync(
        User user,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken)
    {
        var hasAdminRole = roles.Contains(AdminRoleValue, StringComparer.OrdinalIgnoreCase);
        if (hasAdminRole != user.IsAdmin)
        {
            LogAdminRoleSync(hasAdminRole ? "Granting" : "Revoking", user.AuthProvider, user.Username);
            await _userService.SetAdminAsync(user.Id, hasAdminRole, cancellationToken);
            user.IsAdmin = hasAdminRole;
        }

        await _groupService.SyncAppRoleMembershipsAsync(user.Id, roles, cancellationToken);
    }

    private async Task<bool> IsUsernameTakenAsync(string username, Guid? userId, CancellationToken cancellationToken)
    {
        var existing = await _userService.FindByUsernameAsync(username, cancellationToken);
        return existing != null && existing.Id != userId;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Provisioning new {AuthProvider} user: {Username} (ID: {ExternalId})")]
    private partial void LogProvisioningUser(AuthProvider authProvider, string username, string externalId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login denied: the username {Username} of the new {AuthProvider} user is already used by another account")]
    private partial void LogUsernameTaken(AuthProvider authProvider, string username);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Action} admin for {AuthProvider} user {Username} based on its roles")]
    private partial void LogAdminRoleSync(string action, AuthProvider authProvider, string username);
}
