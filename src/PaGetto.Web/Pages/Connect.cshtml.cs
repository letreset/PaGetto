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

public class ConnectModel : PageModel
{
    private readonly IPermissionService _permissions;
    private readonly IFeedContext _feedContext;
    private readonly IOptionsSnapshot<NugetAuthenticationOptions> _authOptions;

    public ConnectModel(
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
        if (FeedAccessGuard.RequiresSignIn(HttpContext, _authOptions.Value.Mode)) return Page();

        var denied = await FeedAccessGuard.CheckReadAccessAsync(
            HttpContext, _feedContext, _permissions, _authOptions.Value.Mode, cancellationToken);
        if (denied != null) return denied;

        return Page();
    }
}
