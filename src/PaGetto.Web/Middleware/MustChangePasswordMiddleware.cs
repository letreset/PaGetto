using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using PaGetto.Core.Authentication;

namespace PaGetto.Web.Middleware;

/// <summary>
/// Keeps a signed-in user who still has to change their password on the change-password page:
/// page requests are redirected there, anything else is rejected with 403.
/// </summary>
public class MustChangePasswordMiddleware
{
    public const string ChangePasswordPath = "/Account/ChangePassword";

    private static readonly PathString[] AllowedPaths =
    [
        new(ChangePasswordPath),
        new("/Login"),
        new("/Logout"),
    ];

    private readonly RequestDelegate _next;

    public MustChangePasswordMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public Task Invoke(HttpContext context)
    {
        if (context.User.HasClaim(AuthenticationConstants.MustChangePasswordClaim, "true") && !IsAllowed(context.Request.Path))
        {
            if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            {
                var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
                var target = FeedResolutionMiddleware.GetRootPathBase(context) + ChangePasswordPath
                    + QueryString.Create("returnUrl", returnUrl);
                context.Response.Redirect(target);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            }

            return Task.CompletedTask;
        }

        return _next(context);
    }

    private static bool IsAllowed(PathString path)
    {
        foreach (var allowed in AllowedPaths)
        {
            if (path.StartsWithSegments(allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
