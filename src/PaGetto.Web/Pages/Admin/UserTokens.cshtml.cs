using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Web.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PaGetto.Web.Pages.Admin;

/// <summary>
/// Lists another user's personal access tokens and lets an administrator revoke one or all of them.
/// </summary>
[Authorize(AuthenticationSchemes = Core.Authentication.AuthenticationConstants.CookieScheme)]
public class UserTokensModel : PageModel
{
    private readonly IUserService _userService;
    private readonly ITokenService _tokenService;
    private readonly SystemTime _systemTime;
    private readonly WebAuditLog _audit;

    public UserTokensModel(IUserService userService, ITokenService tokenService, SystemTime systemTime, WebAuditLog audit)
    {
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _systemTime = systemTime ?? throw new ArgumentNullException(nameof(systemTime));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public User TargetUser { get; private set; }
    public List<PersonalAccessToken> Tokens { get; private set; } = new();
    public DateTime NowUtc { get; private set; }

    public string SuccessMessage { get; set; }
    public string ErrorMessage { get; set; }

    public bool IsActive(PersonalAccessToken token)
    {
        return !token.IsRevoked && token.ExpiresAtUtc > NowUtc;
    }

    public async Task<IActionResult> OnGetAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        return await LoadAsync(userId, cancellationToken) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid userId, Guid tokenId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        if (!await LoadAsync(userId, cancellationToken))
            return NotFound();

        // Only a token of the user on this page: the token id comes from the form.
        var token = Tokens.FirstOrDefault(t => t.Id == tokenId);
        if (token == null || token.IsRevoked)
        {
            ErrorMessage = "Token not found.";
            return Page();
        }

        await _tokenService.RevokeTokenAsync(token.Id, cancellationToken);
        await _audit.AdminAsync(HttpContext, "token_revoked", TargetUser.Username, $"token={token.Name} prefix={token.TokenPrefix}");
        SuccessMessage = $"Token '{token.Name}' revoked.";

        await LoadAsync(userId, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        if (!await LoadAsync(userId, cancellationToken))
            return NotFound();

        var active = Tokens.Where(IsActive).ToList();
        if (active.Count == 0)
        {
            ErrorMessage = $"'{TargetUser.Username}' has no active tokens.";
            return Page();
        }

        foreach (var token in active)
        {
            await _tokenService.RevokeTokenAsync(token.Id, cancellationToken);
        }

        await _audit.AdminAsync(HttpContext, "tokens_revoked_all", TargetUser.Username, $"count={active.Count}");
        SuccessMessage = active.Count == 1 ? "1 token revoked." : $"{active.Count} tokens revoked.";

        await LoadAsync(userId, cancellationToken);
        return Page();
    }

    /// <returns>False when the user doesn't exist.</returns>
    private async Task<bool> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        TargetUser = await _userService.FindByIdAsync(userId, cancellationToken);
        if (TargetUser == null)
            return false;

        Tokens = await _tokenService.GetUserTokensAsync(userId, cancellationToken);
        NowUtc = _systemTime.UtcNow;
        return true;
    }

    private async Task<bool> IsCurrentUserAdminAsync(CancellationToken cancellationToken)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null || !Guid.TryParse(claim.Value, out var userId)) return false;
        return await _userService.IsAdminAsync(userId, cancellationToken);
    }
}
