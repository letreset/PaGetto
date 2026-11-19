using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace PaGetto.Web.Middleware;

/// <summary>
/// Lets VS Code read the gallery of a VS Code feed that needs credentials. The editors only send
/// the URLs of <c>product.json</c>, never a username and password, so the gallery has a second URL
/// with a personal access token in its path: <c>/feeds/{slug}/vscode/auth/{token}/gallery</c>.
/// This middleware runs before authentication, moves the token of a <c>GET</c> or <c>HEAD</c>
/// request, or of the <c>POST</c> of a query, to the <c>Authorization</c> header as a Bearer token
/// and removes it from the path, so the request is authenticated and authorized as that token. The
/// URLs of the gallery's answers keep the token (see <see cref="TokenItem"/>). Other requests are
/// left alone and find no endpoint, so the token URL can't upload.
/// </summary>
public partial class VsCodeGalleryTokenMiddleware
{
    /// <summary>
    /// The key of <see cref="HttpContext.Items"/> that holds the token of a request made through
    /// the token URL.
    /// </summary>
    public const string TokenItem = "PaGetto.VsCodeGalleryToken";

    private const string QueryPath = "/gallery/extensionquery";

    private readonly RequestDelegate _next;

    public VsCodeGalleryTokenMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var match = TokenPathRegex().Match(request.Path.Value ?? "");
        if (match.Success
            && (HttpMethods.IsGet(request.Method)
                || HttpMethods.IsHead(request.Method)
                || (HttpMethods.IsPost(request.Method) && string.Equals(match.Groups["rest"].Value, QueryPath, StringComparison.OrdinalIgnoreCase))))
        {
            var token = match.Groups["token"].Value;
            context.Items[TokenItem] = token;
            request.Path = match.Groups["feed"].Value + "/vscode" + match.Groups["rest"].Value;

            if (!request.Headers.ContainsKey("Authorization"))
            {
                request.Headers.Authorization = "Bearer " + token;
            }
        }

        return _next(context);
    }

    [GeneratedRegex("^(?<feed>/feeds/[^/]+)?/vscode/auth/(?<token>[^/]+)(?<rest>/.*)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TokenPathRegex();
}
