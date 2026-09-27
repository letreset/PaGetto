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
                // A fixed target: nothing from the request goes into the redirect.
                context.Response.Redirect(FeedResolutionMiddleware.GetRootPathBase(context) + ChangePasswordPath);
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
