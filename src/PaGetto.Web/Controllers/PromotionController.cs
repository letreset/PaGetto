using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PaGetto.Core.Feeds;
using PaGetto.Core.Quarantine;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;

namespace PaGetto.Web.Controllers;

/// <summary>
/// Promotes a stored version of the current feed to another feed of the same type:
/// <c>POST {feed}/api/v1/promote</c> with a JSON <see cref="PromotionRequest"/>.
/// </summary>
public class PromotionController : Controller
{
    private readonly IFeedContext _feedContext;
    private readonly IFeedService _feeds;
    private readonly PromotionAuthorizer _authorizer;
    private readonly PromotionService _promotions;

    public PromotionController(IFeedContext feedContext, IFeedService feeds, PromotionAuthorizer authorizer, PromotionService promotions)
    {
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _feeds = feeds ?? throw new ArgumentNullException(nameof(feeds));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _promotions = promotions ?? throw new ArgumentNullException(nameof(promotions));
    }

    [HttpPost]
    public async Task<IActionResult> Promote([FromBody] PromotionRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.PackageId) || string.IsNullOrWhiteSpace(request.Version) || string.IsNullOrWhiteSpace(request.TargetFeed))
        {
            return Problem("packageId, version and targetFeed are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var source = _feedContext.CurrentFeed;
        var target = await _feeds.GetFeedBySlugAsync(request.TargetFeed.Trim(), cancellationToken);
        if (target == null)
        {
            return Problem($"There is no feed {request.TargetFeed}.", statusCode: StatusCodes.Status404NotFound);
        }

        var access = await _authorizer.CheckAsync(User, Request.GetApiKey(), source, target, request.Move, cancellationToken);
        if (access == FeedChangeAccess.Unauthorized)
        {
            return User.Identity?.IsAuthenticated == true ? Forbid() : Unauthorized();
        }

        if (access == FeedChangeAccess.ReadOnly)
        {
            return Problem("The feed is read-only.", statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await _promotions.PromoteAsync(source, target, request.PackageId.Trim(), request.Version.Trim(), request.Move, GetActor(), cancellationToken);

        return result switch
        {
            PromotionResult.Success => Ok(new { packageId = request.PackageId.Trim(), version = request.Version.Trim(), sourceFeed = source.Slug, targetFeed = target.Slug, moved = request.Move }),
            PromotionResult.NotFound => Problem($"Feed {source.Slug} has no version {request.Version} of {request.PackageId}.", statusCode: StatusCodes.Status404NotFound),
            PromotionResult.AlreadyExists => Problem($"Feed {target.Slug} already has version {request.Version} of {request.PackageId}.", statusCode: StatusCodes.Status409Conflict),
            PromotionResult.InvalidTarget => Problem("The target must be another feed of the same type.", statusCode: StatusCodes.Status400BadRequest),
            _ => Problem($"Feed {target.Slug} refused the package.", statusCode: StatusCodes.Status400BadRequest),
        };
    }

    private string GetActor()
    {
        var name = User.Identity?.Name;
        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        return string.IsNullOrEmpty(Request.GetApiKey()) ? "anonymous" : "api-key";
    }
}
