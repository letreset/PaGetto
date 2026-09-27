using System;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Feeds;
using PaGetto.Web.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace PaGetto.Web.Pages;

public class UploadModel : PageModel
{
    private readonly IPermissionService _permissions;
    private readonly IFeedContext _feedContext;
    private readonly IOptionsSnapshot<NugetAuthenticationOptions> _authOptions;

    public UploadModel(
        IPermissionService permissions,
        IFeedContext feedContext,
        IOptionsSnapshot<NugetAuthenticationOptions> authOptions)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _authOptions = authOptions ?? throw new ArgumentNullException(nameof(authOptions));
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var authMode = _authOptions.Value.Mode;
        if (FeedAccessGuard.RequiresSignIn(HttpContext, authMode)) return Page();

        var denied = await FeedAccessGuard.CheckReadAccessAsync(
            HttpContext, _feedContext, _permissions, authMode, cancellationToken);
        if (denied != null) return denied;

        // The page only helps users who can push, and the Upload link is hidden for everyone
        // else, so pull-only users get the same 404 as a missing page.
        if (!await FeedAccessGuard.CanPushToCurrentFeedAsync(
                HttpContext, _feedContext, _permissions, authMode, cancellationToken))
        {
            return NotFound();
        }

        return Page();
    }
}
