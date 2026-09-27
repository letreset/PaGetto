using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Feeds;
using PaGetto.Core.Search;
using PaGetto.Protocol.Models;
using PaGetto.Web.Authentication;
using PaGetto.Web.Helper;
using PaGetto.Web.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace PaGetto.Web.Pages;

public class IndexModel : PageModel
{
    private readonly ISearchService _search;
    private readonly IFeedContext _feedContext;
    private readonly IFeedService _feedService;
    private readonly IPermissionService _permissions;
    private readonly IOptionsSnapshot<NugetAuthenticationOptions> _authOptions;

    public IndexModel(
        ISearchService search,
        IFeedContext feedContext,
        IFeedService feedService,
        IPermissionService permissions,
        IOptionsSnapshot<NugetAuthenticationOptions> authOptions)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _feedService = feedService ?? throw new ArgumentNullException(nameof(feedService));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _authOptions = authOptions ?? throw new ArgumentNullException(nameof(authOptions));
    }

    public bool HasNoAccessibleFeeds { get; private set; }

    private Guid GetUserIdOrEmpty()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        return claim != null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
    }

    public const int ResultsPerPage = 20;

    [BindProperty(Name = "q", SupportsGet = true)]
    public string Query { get; set; }

    [BindProperty(Name = "p", SupportsGet = true)]
    [Range(1, int.MaxValue)]
    public int PageIndex { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string PackageType { get; set; } = "any";

    [BindProperty(SupportsGet = true)]
    public string Framework { get; set; } = "any";

    [BindProperty(SupportsGet = true)]
    public string Tag { get; set; } = "any";

    [BindProperty(SupportsGet = true)]
    public bool Prerelease { get; set; } = true;

    public IReadOnlyList<SearchResult> Packages { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public long TotalHits { get; private set; }

    private SearchFacets Facets { get; set; }

    public IReadOnlyList<string> PackageTypeFacets => Facets?.PackageTypes ?? Array.Empty<string>();

    public IReadOnlyList<string> FrameworkFacets => TargetFrameworkNames.Sort(Facets?.Frameworks ?? Array.Empty<string>());

    public IReadOnlyList<string> TagFacets => Facets?.Tags ?? Array.Empty<string>();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest();

        var authMode = _authOptions.Value.Mode;
        if (FeedAccessGuard.RequiresSignIn(HttpContext, authMode)) return Page();

        var currentFeed = _feedContext.CurrentFeed;

        // For Local/Entra/Hybrid modes, decide which feed the signed-in user lands on.
        // On the root route, feed ordering always wins: redirect to the first feed (by
        // SortOrder) the user can pull, even when they could access the default-slug feed.
        // On an explicit /feeds/{slug} route, only redirect away when the user can't pull
        // the requested feed. Unauthenticated visitors returned above; the view renders a
        // "Sign in required" prompt for them.
        if (authMode != AuthenticationMode.Legacy)
        {
            var mustSelectLandingFeed = _feedContext.IsDefaultRoute
                || currentFeed == null
                || !await _permissions.CanPullAsync(GetUserIdOrEmpty(), currentFeed.Id, cancellationToken);

            if (mustSelectLandingFeed)
            {
                var allFeeds = await _feedService.GetAllFeedsAsync(cancellationToken);
                var accessible = await FeedAccessGuard.FilterAccessibleFeedsAsync(
                    HttpContext, allFeeds, _permissions, authMode, cancellationToken);

                if (accessible.Count == 0)
                {
                    HasNoAccessibleFeeds = true;
                    Packages = Array.Empty<SearchResult>();
                    return Page();
                }

                var target = accessible.First();

                // Redirect unless we're already on the target feed, which avoids a redirect
                // loop when the default-slug feed (served at "/") is itself the target.
                // Every feed — including the default — has a stable /feeds/{slug} URL so it
                // stays reachable even though "/" always redirects to the first accessible feed.
                if (target.Id != currentFeed?.Id)
                {
                    var targetPath = FeedResolutionMiddleware.GetRootPathBase(HttpContext).Add($"/feeds/{target.Slug}/");
                    return Redirect(targetPath.ToUriComponent());
                }
            }
        }

        var packageType = PackageType == "any" ? null : PackageType;
        var framework = Framework == "any" ? null : Framework;
        var tag = Tag == "any" ? null : Tag;

        // Users who can relist also see fully-unlisted packages (struck through) so they can find
        // and restore them; everyone else only sees listed packages.
        var canManage = await FeedAccessGuard.CanDeleteFromCurrentFeedAsync(
            HttpContext, _feedContext, _permissions, authMode, cancellationToken);

        var search = await _search.SearchAsync(
            new SearchRequest
            {
                FeedId = _feedContext.CurrentFeed.Id,
                Skip = (PageIndex - 1) * ResultsPerPage,
                Take = ResultsPerPage,
                IncludePrerelease = Prerelease,
                IncludeSemVer2 = true,
                PackageType = packageType,
                Framework = framework,
                Tag = tag,
                IncludeFacets = true,
                IncludeUnlisted = canManage,
                Query = Query,
            },
            cancellationToken);

        Packages = search.Data;
        Facets = search.Facets;
        TotalHits = search.TotalHits;
        TotalPages = Math.Max(1, (int)Math.Ceiling(search.TotalHits / (double)ResultsPerPage));

        return Page();
    }
}
