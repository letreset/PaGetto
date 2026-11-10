using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PaGetto.Core.Configuration;
using PaGetto.Core.Feeds;

namespace PaGetto.Web.Middleware;

/// <summary>
/// Answers 404 when an endpoint of one package ecosystem (see <see cref="FeedTypeAttribute"/>) is
/// requested on a feed of another type, e.g. the NuGet API on a Dart feed. Runs after
/// authorization, so an anonymous request is challenged first.
/// </summary>
public class FeedTypeMiddleware
{
    private readonly RequestDelegate _next;

    public FeedTypeMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public Task InvokeAsync(HttpContext context, IFeedContext feedContext, IOptionsSnapshot<PaGettoOptions> options)
    {
        var endpointType = context.GetEndpoint()?.Metadata.GetMetadata<FeedTypeAttribute>()?.Type;
        var feed = feedContext.CurrentFeed;

        if (endpointType.HasValue && feed != null && feed.Type != endpointType.Value && CanKnowFeedType(context, options.Value))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return _next(context);
    }

    /// <summary>
    /// With user accounts, an anonymous caller must not learn whether a feed exists, and a 404 here
    /// would tell a feed of another type from a missing one (which FeedResolutionMiddleware replaces
    /// with a stand-in NuGet feed). The endpoints reject anonymous callers themselves, so the
    /// request goes on to them.
    /// </summary>
    private static bool CanKnowFeedType(HttpContext context, PaGettoOptions options)
    {
        var authMode = options.Authentication?.Mode ?? AuthenticationMode.Legacy;

        return authMode == AuthenticationMode.Legacy
            || context.User.Identity?.IsAuthenticated == true;
    }
}
