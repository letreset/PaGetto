using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace PaGetto.Web.Pages.Account;

[Authorize(AuthenticationSchemes = Core.Authentication.AuthenticationConstants.CookieScheme)]
public class TokensModel : PageModel
{
    private readonly ITokenService _tokenService;
    private readonly IFeedService _feedService;
    private readonly IPermissionService _permissionService;
    private readonly IOptionsSnapshot<NugetAuthenticationOptions> _authOptions;

    public TokensModel(
        ITokenService tokenService,
        IFeedService feedService,
        IPermissionService permissionService,
        IOptionsSnapshot<NugetAuthenticationOptions> authOptions)
    {
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _feedService = feedService ?? throw new ArgumentNullException(nameof(feedService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _authOptions = authOptions ?? throw new ArgumentNullException(nameof(authOptions));
    }

    /// <summary>
    /// What a new token may do. Every token can read what its user can read.
    /// </summary>
    public enum TokenAccess
    {
        Read,
        Publish,
        PublishAndDelete,
    }

    public List<PersonalAccessToken> Tokens { get; set; } = new();

    /// <summary>
    /// The feeds the user can read, which a token can be limited to.
    /// </summary>
    public List<Feed> Feeds { get; set; } = new();

    /// <summary>
    /// The names of all feeds by id, to describe the feed a token is limited to.
    /// </summary>
    public Dictionary<Guid, string> FeedNames { get; set; } = new();

    [BindProperty]
    public TokenAccess Access { get; set; } = TokenAccess.PublishAndDelete;

    /// <summary>
    /// The only feed the new token may be used with, or null for every feed.
    /// </summary>
    [BindProperty]
    public Guid? FeedId { get; set; }

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

        await LoadAsync(userId, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return RedirectToPage("/Login");

        if (!ModelState.IsValid)
        {
            await LoadAsync(userId, cancellationToken);
            return Page();
        }

        if (FeedId is { } feedId && !await _permissionService.CanPullAsync(userId, feedId, cancellationToken))
        {
            ErrorMessage = "You can only limit a token to a feed you can read.";
            return RedirectToPage();
        }

        var maxDays = _authOptions.Value.MaxTokenExpiryDays;
        if (ExpiryDays > maxDays)
        {
            ExpiryDays = maxDays;
        }

        try
        {
            var permissions = new TokenPermissions(
                CanPush: Access != TokenAccess.Read,
                CanDelete: Access == TokenAccess.PublishAndDelete,
                FeedId: FeedId);

            var result = await _tokenService.CreateTokenAsync(
                userId,
                TokenName,
                DateTime.UtcNow.AddDays(ExpiryDays),
                permissions,
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

    private async Task LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        Tokens = await _tokenService.GetUserTokensAsync(userId, cancellationToken);

        var feeds = await _feedService.GetAllFeedsAsync(cancellationToken);
        FeedNames = feeds.ToDictionary(f => f.Id, f => f.Name);

        Feeds.Clear();
        foreach (var feed in feeds.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (await _permissionService.CanPullAsync(userId, feed.Id, cancellationToken))
            {
                Feeds.Add(feed);
            }
        }
    }
}
