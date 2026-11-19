using System;
using Microsoft.AspNetCore.Mvc;
using PaGetto.Web.Middleware;

namespace PaGetto.Web.Extensions;

public static class VsCodeUrlHelperExtensions
{
    /// <summary>
    /// The absolute URL of the gallery of the current feed, below which its paths are:
    /// <c>https://host/feeds/{slug}/vscode</c>, or for a request made through the token URL,
    /// <c>https://host/feeds/{slug}/vscode/auth/{token}</c>, so the URLs of the gallery's answers
    /// carry the token too.
    /// </summary>
    public static string VsCodeGallery(this IUrlHelper url)
    {
        var context = url.ActionContext.HttpContext;
        var request = context.Request;
        var gallery = $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}/vscode";

        return context.Items.TryGetValue(VsCodeGalleryTokenMiddleware.TokenItem, out var token) && token is string text
            ? $"{gallery}/auth/{Uri.EscapeDataString(text)}"
            : gallery;
    }

    /// <summary>
    /// The base URL of the gallery with a personal access token in place of
    /// <paramref name="tokenPlaceholder"/>, which the editors read when the feed needs credentials.
    /// </summary>
    public static string VsCodeGalleryWithToken(this IUrlHelper url, string tokenPlaceholder)
    {
        return $"{url.VsCodeGallery()}/auth/{tokenPlaceholder}";
    }
}
