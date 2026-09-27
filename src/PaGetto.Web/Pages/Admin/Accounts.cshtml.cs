using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Web.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace PaGetto.Web.Pages.Admin;

[Authorize(AuthenticationSchemes = Core.Authentication.AuthenticationConstants.CookieScheme)]
public class AccountsModel : PageModel
{
    private readonly IUserService _userService;
    private readonly IGroupService _groupService;
    private readonly ITokenService _tokenService;
    private readonly IOptionsSnapshot<NugetAuthenticationOptions> _authOptions;
    private readonly WebAuditLog _audit;

    public AccountsModel(
        IUserService userService,
        IGroupService groupService,
        ITokenService tokenService,
        IOptionsSnapshot<NugetAuthenticationOptions> authOptions,
        WebAuditLog audit)
    {
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _groupService = groupService ?? throw new ArgumentNullException(nameof(groupService));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _authOptions = authOptions ?? throw new ArgumentNullException(nameof(authOptions));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public List<User> Users { get; set; } = new();
    public Dictionary<Guid, List<Group>> UserGroupMemberships { get; set; } = new();

    [BindProperty]
    [Required(ErrorMessage = "Username is required.")]
    [MaxLength(256)]
    public string NewUsername { get; set; }

    [BindProperty]
    [MaxLength(256)]
    public string NewDisplayName { get; set; }

    [BindProperty]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [MaxLength(256)]
    public string NewEmail { get; set; }


    [BindProperty]
    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; }

    [BindProperty]
    public bool NewCanLoginToUI { get; set; }

    public int MinPasswordLength => _authOptions.Value.MinPasswordLength;

    public string SuccessMessage { get; set; }

    /// <summary>The result of an action that redirects back to this page, shown once as a toast.</summary>
    [TempData]
    public string ToastMessage { get; set; }

    public string NewTokenPlaintext { get; set; }
    public string ErrorMessage { get; set; }

    /// <summary>The signed-in administrator. Their own row doesn't offer actions that lock them out.</summary>
    public Guid CurrentUserId => GetUserId();

    /// <summary>
    /// An administrator who can manage the server: enabled and allowed to sign in to the web UI.
    /// </summary>
    public static bool IsActiveAdmin(User user)
    {
        return user.IsAdmin && user.IsEnabled && user.CanLoginToUI;
    }

    public static bool IsLocked(User user)
    {
        return user.LockedUntilUtc > DateTime.UtcNow;
    }

    /// <summary>
    /// Returns an error when taking <paramref name="userId"/>'s admin access away (disable, revoke
    /// web sign-in, remove admin) would lock the current user or everybody out of administration.
    /// </summary>
    private async Task<string> CheckKeepsAdminAccessAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == GetUserId())
            return "You can't take away your own administrator access. Ask another administrator.";

        var users = await _userService.GetAllUsersAsync(cancellationToken);
        var target = users.FirstOrDefault(u => u.Id == userId);
        if (target != null && IsActiveAdmin(target) && users.Count(IsActiveAdmin) == 1)
            return $"'{target.Username}' is the last enabled administrator.";

        return null;
    }

    /// <summary>
    /// The create form's properties are bound (and validated) for every POST. Only the Create
    /// handler uses them, so other handlers drop their errors instead of showing
    /// "Username is required." under the create form.
    /// </summary>
    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if (context.HandlerMethod?.MethodInfo.Name != nameof(OnPostCreateAsync))
        {
            ModelState.Clear();
        }
    }

    /// <returns>The account's username, for the result message.</returns>
    private async Task<string> AuditAsync(string eventName, Guid userId, CancellationToken cancellationToken, string detail = null)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        var username = user?.Username ?? userId.ToString();
        await _audit.AdminAsync(HttpContext, eventName, username, detail);
        return username;
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
    }

    private async Task<bool> IsCurrentUserAdminAsync(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return false;
        return await _userService.IsAdminAsync(userId, cancellationToken);
    }

    private async Task LoadUsersAndGroupsAsync(CancellationToken cancellationToken)
    {
        Users = await _userService.GetAllUsersAsync(cancellationToken);
        var allGroups = await _groupService.GetAllGroupsAsync(cancellationToken);
        UserGroupMemberships = allGroups
            .Where(g => g.UserGroups != null)
            .SelectMany(g => g.UserGroups.Select(ug => new { ug.UserId, Group = g }))
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Group).ToList());
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        await LoadUsersAndGroupsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        if (NewPassword?.Length < MinPasswordLength)
            ModelState.AddModelError(nameof(NewPassword), $"Password must be at least {MinPasswordLength} characters.");

        if (!ModelState.IsValid)
        {
            await LoadUsersAndGroupsAsync(cancellationToken);
            return Page();
        }

        var existing = await _userService.FindByUsernameAsync(NewUsername, cancellationToken);
        if (existing != null)
        {
            ErrorMessage = $"Username '{NewUsername}' already exists.";
            await LoadUsersAndGroupsAsync(cancellationToken);
            return Page();
        }

        await _userService.CreateLocalUserAsync(
            NewUsername,
            NewDisplayName ?? NewUsername,
            NewEmail,
            NewPassword,
            NewCanLoginToUI,
            GetUserId(),
            cancellationToken);

        await _audit.AdminAsync(HttpContext, "account_created", NewUsername, $"web_sign_in={NewCanLoginToUI}");
        SuccessMessage = $"Account '{NewUsername}' created successfully.";
        await LoadUsersAndGroupsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostToggleEnabledAsync(
        Guid userId, bool isEnabled, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        if (isEnabled && await CheckKeepsAdminAccessAsync(userId, cancellationToken) is { } error)
        {
            ErrorMessage = error;
            await LoadUsersAndGroupsAsync(cancellationToken);
            return Page();
        }

        await _userService.SetEnabledAsync(userId, !isEnabled, cancellationToken);
        var username = await AuditAsync(isEnabled ? "account_disabled" : "account_enabled", userId, cancellationToken);
        ToastMessage = isEnabled ? $"Account '{username}' disabled." : $"Account '{username}' enabled.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleCanLoginToUIAsync(
        Guid userId, bool canLoginToUI, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        if (canLoginToUI && await CheckKeepsAdminAccessAsync(userId, cancellationToken) is { } error)
        {
            ErrorMessage = error;
            await LoadUsersAndGroupsAsync(cancellationToken);
            return Page();
        }

        await _userService.SetCanLoginToUIAsync(userId, !canLoginToUI, cancellationToken);
        var username = await AuditAsync(canLoginToUI ? "account_web_access_revoked" : "account_web_access_granted", userId, cancellationToken);
        ToastMessage = canLoginToUI ? $"Web sign-in disabled for '{username}'." : $"Web sign-in allowed for '{username}'.";

        return RedirectToPage();
    }

    /// <summary>
    /// Makes a local account an administrator or removes its admin rights. Entra accounts follow
    /// the Admin app role on every sign-in, so they can't be changed here.
    /// </summary>
    public async Task<IActionResult> OnPostToggleAdminAsync(
        Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        if (user == null || user.AuthProvider != AuthProvider.Local)
        {
            ErrorMessage = "Administrator rights can only be changed here for local accounts.";
            await LoadUsersAndGroupsAsync(cancellationToken);
            return Page();
        }

        if (isAdmin && await CheckKeepsAdminAccessAsync(userId, cancellationToken) is { } error)
        {
            ErrorMessage = error;
            await LoadUsersAndGroupsAsync(cancellationToken);
            return Page();
        }

        await _userService.SetAdminAsync(userId, !isAdmin, cancellationToken);
        await _audit.AdminAsync(HttpContext, isAdmin ? "account_admin_revoked" : "account_admin_granted", user.Username);
        ToastMessage = isAdmin ? $"Administrator role removed from '{user.Username}'." : $"'{user.Username}' is now an administrator.";

        return RedirectToPage();
    }

    /// <summary>
    /// Changes the username, display name and email of a local account. Entra accounts take these
    /// from the directory, so they can't be changed here.
    /// </summary>
    public async Task<IActionResult> OnPostEditAsync(
        Guid userId, string username, string displayName, string email, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        username = username?.Trim();
        displayName = displayName?.Trim();
        email = email?.Trim();

        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        var error = ValidateAccountEdit(user, username, displayName, email);
        if (error == null)
        {
            var existing = await _userService.FindByUsernameAsync(username, cancellationToken);
            if (existing != null && existing.Id != userId)
                error = $"Username '{username}' already exists.";
        }

        if (error != null)
        {
            ErrorMessage = error;
            await LoadUsersAndGroupsAsync(cancellationToken);
            return Page();
        }

        var previousUsername = user.Username;
        user.Username = username;
        user.DisplayName = string.IsNullOrEmpty(displayName) ? username : displayName;
        user.Email = string.IsNullOrEmpty(email) ? null : email;
        await _userService.UpdateUserAsync(user, cancellationToken);

        await _audit.AdminAsync(HttpContext, "account_updated", username,
            previousUsername == username ? null : $"previous_username={previousUsername}");
        ToastMessage = $"Account '{username}' updated.";

        return RedirectToPage();
    }

    // The column lengths of Users.Username, DisplayName and Email.
    private const int MaxAccountFieldLength = 256;

    private static string ValidateAccountEdit(User user, string username, string displayName, string email)
    {
        if (user == null || user.AuthProvider != AuthProvider.Local)
            return "Only local accounts can be edited here. Entra ID accounts take their name and email from the directory.";

        if (string.IsNullOrEmpty(username))
            return "Username is required.";

        if (username.Length > MaxAccountFieldLength)
            return $"Username must be at most {MaxAccountFieldLength} characters.";

        if (displayName?.Length > MaxAccountFieldLength)
            return $"Display name must be at most {MaxAccountFieldLength} characters.";

        if (!string.IsNullOrEmpty(email)
            && (email.Length > MaxAccountFieldLength || !new EmailAddressAttribute().IsValid(email)))
            return "Enter a valid email address.";

        return null;
    }

    public async Task<IActionResult> OnPostUnlockAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        await _userService.ResetFailedLoginCountAsync(userId, cancellationToken);
        var username = await AuditAsync("account_unlocked", userId, cancellationToken);
        ToastMessage = $"Account '{username}' unlocked.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(
        Guid userId, string newPassword, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinPasswordLength)
        {
            ErrorMessage = $"Password must be at least {MinPasswordLength} characters.";
            await LoadUsersAndGroupsAsync(cancellationToken);
            return Page();
        }

        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        if (user == null || user.AuthProvider != AuthProvider.Local)
        {
            ErrorMessage = "Passwords can only be reset for local accounts.";
            await LoadUsersAndGroupsAsync(cancellationToken);
            return Page();
        }

        await _userService.SetPasswordAsync(userId, newPassword, cancellationToken);

        // A new password from an administrator also ends a lockout from earlier failed attempts.
        await _userService.ResetFailedLoginCountAsync(userId, cancellationToken);
        await _audit.AdminAsync(HttpContext, "account_password_reset", user.Username);

        SuccessMessage = $"Password of '{user.Username}' reset successfully.";
        await LoadUsersAndGroupsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            ErrorMessage = "User not found.";
            await LoadUsersAndGroupsAsync(cancellationToken);
            return Page();
        }

        if (user.IsEnabled)
        {
            ErrorMessage = "Cannot delete an enabled account. Disable the account first.";
            await LoadUsersAndGroupsAsync(cancellationToken);
            return Page();
        }

        var username = user.Username;
        await _userService.DeleteUserAsync(userId, cancellationToken);
        await _audit.AdminAsync(HttpContext, "account_deleted", username);

        SuccessMessage = $"Account '{username}' has been deleted.";
        await LoadUsersAndGroupsAsync(cancellationToken);
        return Page();
    }

    /// <summary>
    /// Creates a personal access token for a local account, so that accounts without web sign-in
    /// (build agents) don't have to put their password into nuget.config.
    /// </summary>
    public async Task<IActionResult> OnPostCreateTokenAsync(
        Guid userId, string tokenName, int expiryDays, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        if (user == null || user.AuthProvider != AuthProvider.Local)
        {
            ErrorMessage = "Tokens can only be created here for local accounts.";
            await LoadUsersAndGroupsAsync(cancellationToken);
            return Page();
        }

        if (string.IsNullOrWhiteSpace(tokenName))
        {
            ErrorMessage = "Token name is required.";
            await LoadUsersAndGroupsAsync(cancellationToken);
            return Page();
        }

        var days = Math.Clamp(expiryDays, 1, _authOptions.Value.MaxTokenExpiryDays);
        try
        {
            var result = await _tokenService.CreateTokenAsync(
                userId, tokenName.Trim(), DateTime.UtcNow.AddDays(days), cancellationToken);
            NewTokenPlaintext = result.PlaintextToken;
            await _audit.AdminAsync(HttpContext, "account_token_created", user.Username, $"token={result.Token.TokenPrefix} expires={result.Token.ExpiresAtUtc:yyyy-MM-dd}");
            SuccessMessage = $"Token '{result.Token.Name}' created for '{user.Username}'. Copy it now, it is shown only once.";
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadUsersAndGroupsAsync(cancellationToken);
        return Page();
    }
}
