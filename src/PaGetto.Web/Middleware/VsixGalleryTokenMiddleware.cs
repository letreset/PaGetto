using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace PaGetto.Web.Middleware;

/// <summary>
/// Lets Visual Studio read the gallery of a VSIX feed that needs credentials. Visual Studio only
/// sends the Windows credentials of the user, never a username and password, so the gallery has
/// a second URL with a personal access token in its path:
/// <c>/feeds/{slug}/vsix/auth/{token}/atom.xml</c>. This middleware runs before authentication,
/// moves the token of a <c>GET</c> or <c>HEAD</c> request to the <c>Authorization</c> header as a
/// Bearer token and removes it from the path, so the request is authenticated and authorized as
/// that token. The links of the gallery keep the token (see <see cref="TokenItem"/>). Other
/// methods are left alone and find no endpoint, so the token URL can't upload.
/// </summary>
public partial class VsixGalleryTokenMiddleware
{
    /// <summary>
    /// The key of <see cref="HttpContext.Items"/> that holds the token of a request made through
    /// the token URL.
    /// </summary>
    public const string TokenItem = "PaGetto.VsixGalleryToken";

    private readonly RequestDelegate _next;

    public VsixGalleryTokenMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public Task InvokeAsync(HttpContext context)
    {
        var match = TokenPathRegex().Match(context.Request.Path.Value ?? "");
        if (match.Success && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
        {
            var token = match.Groups["token"].Value;
            context.Items[TokenItem] = token;
            context.Request.Path = match.Groups["feed"].Value + "/vsix" + match.Groups["rest"].Value;

            if (!context.Request.Headers.ContainsKey("Authorization"))
            {
                context.Request.Headers.Authorization = "Bearer " + token;
            }
        }

        return _next(context);
    }

    [GeneratedRegex("^(?<feed>/feeds/[^/]+)?/vsix/auth/(?<token>[^/]+)(?<rest>/.*)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TokenPathRegex();
}
