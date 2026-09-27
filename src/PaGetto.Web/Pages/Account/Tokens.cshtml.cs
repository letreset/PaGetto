using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace PaGetto.Web.Pages.Account;

[Authorize(AuthenticationSchemes = Core.Authentication.AuthenticationConstants.CookieScheme)]
public class TokensModel : PageModel
{
    private readonly ITokenService _tokenService;
    private readonly IOptionsSnapshot<NugetAuthenticationOptions> _authOptions;

    public TokensModel(
        ITokenService tokenService,
        IOptionsSnapshot<NugetAuthenticationOptions> authOptions)
    {
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _authOptions = authOptions ?? throw new ArgumentNullException(nameof(authOptions));
    }

    public List<PersonalAccessToken> Tokens { get; set; } = new();

    [BindProperty]
    [Required(ErrorMessage = "Token name is required.")]
    [MaxLength(256)]
    public string TokenName { get; set; }

    [BindProperty]
    [Range(1, 365)]
    public int ExpiryDays { get; set; } = 90;

    [TempData]
    public string NewTokenPlaintext { get; set; }

    [TempData]
    public string ErrorMessage { get; set; }

    public int MaxTokenExpiryDays => _authOptions.Value.MaxTokenExpiryDays;

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return RedirectToPage("/Login");

        Tokens = await _tokenService.GetUserTokensAsync(userId, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return RedirectToPage("/Login");

        if (!ModelState.IsValid)
        {
            Tokens = await _tokenService.GetUserTokensAsync(userId, cancellationToken);
            return Page();
        }

        var maxDays = _authOptions.Value.MaxTokenExpiryDays;
        if (ExpiryDays > maxDays)
        {
            ExpiryDays = maxDays;
        }

        try
        {
            var result = await _tokenService.CreateTokenAsync(
                userId,
                TokenName,
                DateTime.UtcNow.AddDays(ExpiryDays),
                cancellationToken);

            NewTokenPlaintext = result.PlaintextToken;
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid tokenId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return RedirectToPage("/Login");

        // Only revoke the signed-in user's own tokens.
        var ownTokens = await _tokenService.GetUserTokensAsync(userId, cancellationToken);
        if (ownTokens.Exists(t => t.Id == tokenId))
        {
            await _tokenService.RevokeTokenAsync(tokenId, cancellationToken);
        }

        return RedirectToPage();
    }
}
