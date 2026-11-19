using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.MaliciousPackages;
using PaGetto.Web.Audit;

namespace PaGetto.Web.Middleware;

/// <summary>
/// Answers a download that <see cref="MaliciousPackageGuard"/> refused with 403 and a message in
/// the format the feed's client shows, and records it in the audit log, which also sends it to the
/// feed's webhook.
/// </summary>
public class MaliciousPackageMiddleware
{
    private readonly RequestDelegate _next;

    public MaliciousPackageMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context, IFeedContext feedContext, WebAuditLog audit)
    {
        try
        {
            await _next(context);
        }
        catch (MaliciousPackageBlockedException e) when (!context.Response.HasStarted)
        {
            await audit.MaliciousPackageBlockedAsync(context, e.FeedSlug, e.PackageName, e.Version, e.AdvisoryId);
            await WriteAsync(context, feedContext.CurrentFeed?.Type, e.Message);
        }
    }

    private static async Task WriteAsync(HttpContext context, FeedType? feedType, string message)
    {
        var response = context.Response;
        response.Clear();
        response.StatusCode = StatusCodes.Status403Forbidden;

        switch (feedType)
        {
            case FeedType.Npm:
                // npm shows the error of a failed request.
                response.ContentType = "application/json";
                await response.WriteAsync(new JsonObject { ["error"] = message }.ToJsonString());
                break;

            case FeedType.Pub:
                // The error object of the pub repository spec.
                response.ContentType = "application/vnd.pub.v2+json";
                await response.WriteAsync(new JsonObject
                {
                    ["error"] = new JsonObject { ["code"] = "PackageBlocked", ["message"] = message },
                }.ToJsonString());
                break;

            case FeedType.Cargo:
                // The error list of Cargo's registry API.
                response.ContentType = "application/json";
                await response.WriteAsync(new JsonObject
                {
                    ["errors"] = new JsonArray(new JsonObject { ["detail"] = message }),
                }.ToJsonString());
                break;

            default:
                // NuGet, Maven, Ivy and PyPI clients show the reason phrase; the go command,
                // Bundler, Composer, R and Mix show the body.
                var feature = context.Features.Get<IHttpResponseFeature>();
                if (feature != null)
                {
                    // A reason phrase is printable ASCII.
                    feature.ReasonPhrase = new string(message.Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray());
                }

                response.ContentType = "text/plain; charset=utf-8";
                await response.WriteAsync(message);
                break;
        }
    }
}
