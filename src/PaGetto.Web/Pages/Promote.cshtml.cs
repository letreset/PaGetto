using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Quarantine;
using PaGetto.Web.Authentication;

namespace PaGetto.Web.Pages;

/// <summary>
/// Copies or moves a stored version of the current feed to another feed of the same type.
/// </summary>
public class PromoteModel : PageModel
{
    private readonly IFeedContext _feedContext;
    private readonly IFeedService _feeds;
    private readonly PromotionAuthorizer _authorizer;
    private readonly PromotionService _promotions;

    public PromoteModel(IFeedContext feedContext, IFeedService feeds, PromotionAuthorizer authorizer, PromotionService promotions)
    {
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _feeds = feeds ?? throw new ArgumentNullException(nameof(feeds));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _promotions = promotions ?? throw new ArgumentNullException(nameof(promotions));
    }

    /// <summary>
    /// The package as the feed stores it (the id of a NuGet package).
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Version { get; set; }

    [BindProperty]
    public string TargetFeed { get; set; }

    [BindProperty]
    public bool Move { get; set; }

    public Feed Feed => _feedContext.CurrentFeed;

    public IReadOnlyList<Feed> Targets { get; private set; } = [];

    public string ErrorMessage { get; private set; }

    /// <summary>
    /// The feed the version was promoted to, after a promotion succeeded.
    /// </summary>
    public Feed PromotedTo { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Id) || string.IsNullOrEmpty(Version))
        {
            return NotFound();
        }

        Targets = await _authorizer.GetTargetsAsync(User, Feed, cancellationToken);
        if (Targets.Count == 0)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Id) || string.IsNullOrEmpty(Version))
        {
            return NotFound();
        }

        Targets = await _authorizer.GetTargetsAsync(User, Feed, cancellationToken);
        var target = Targets.FirstOrDefault(f => f.Slug == TargetFeed);
        if (target == null)
        {
            ErrorMessage = "Choose a feed to promote the version to.";
            return Page();
        }

        // GetTargetsAsync has checked the push permission and read-only mode of the target only.
        var access = await _authorizer.CheckAsync(User, apiKey: null, Feed, target, Move, cancellationToken);
        if (access != FeedChangeAccess.Allowed)
        {
            ErrorMessage = access == FeedChangeAccess.ReadOnly
                ? "This feed is read-only, so the version can only be copied."
                : "You may only copy this version: moving it needs the delete permission on this feed.";
            return Page();
        }

        var result = await _promotions.PromoteAsync(Feed, target, Id, Version, Move, User.Identity?.Name, cancellationToken);
        switch (result)
        {
            case PromotionResult.Success:
                PromotedTo = target;
                break;
            case PromotionResult.AlreadyExists:
                ErrorMessage = $"Feed {target.Name} already has version {Version}.";
                break;
            case PromotionResult.NotFound:
                ErrorMessage = $"This feed has no stored version {Version} of {Id}.";
                break;
            default:
                ErrorMessage = $"Feed {target.Name} refused the package.";
                break;
        }

        return Page();
    }
}
