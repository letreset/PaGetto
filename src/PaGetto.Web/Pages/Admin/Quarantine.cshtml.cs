using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PaGetto.Core.Authentication;
using PaGetto.Core.Quarantine;

namespace PaGetto.Web.Pages.Admin;

/// <summary>
/// The quarantined versions of every feed, which an administrator releases or deletes.
/// </summary>
[Authorize(AuthenticationSchemes = AuthenticationConstants.CookieScheme)]
public class QuarantineModel : PageModel
{
    private readonly QuarantineService _quarantine;
    private readonly IUserService _userService;

    public QuarantineModel(QuarantineService quarantine, IUserService userService)
    {
        _quarantine = quarantine ?? throw new ArgumentNullException(nameof(quarantine));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
    }

    public IReadOnlyList<QuarantinedVersion> Versions { get; private set; } = [];

    public string SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        Versions = await _quarantine.ListAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostReleaseAsync(bool isNuGet, int key, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        var released = await _quarantine.ReleaseAsync(isNuGet, key, User.Identity?.Name, cancellationToken);
        if (released != null)
        {
            SuccessMessage = $"{released.Name} {released.Version} is released in feed {released.Feed.Name}.";
        }

        Versions = await _quarantine.ListAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(bool isNuGet, int key, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        var deleted = await _quarantine.DeleteAsync(isNuGet, key, User.Identity?.Name, cancellationToken);
        if (deleted != null)
        {
            SuccessMessage = $"{deleted.Name} {deleted.Version} is deleted from feed {deleted.Feed.Name}.";
        }

        Versions = await _quarantine.ListAsync(cancellationToken);
        return Page();
    }

    private async Task<bool> IsCurrentUserAdminAsync(CancellationToken cancellationToken)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null || !Guid.TryParse(claim.Value, out var userId)) return false;
        return await _userService.IsAdminAsync(userId, cancellationToken);
    }
}
