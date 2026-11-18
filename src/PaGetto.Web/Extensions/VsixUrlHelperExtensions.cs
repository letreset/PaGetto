using System;
using Microsoft.AspNetCore.Mvc;
using PaGetto.Core.Vsix;
using PaGetto.Web.Middleware;

namespace PaGetto.Web.Extensions;

public static class VsixUrlHelperExtensions
{
    /// <summary>
    /// The absolute URL of the gallery of the current feed, below which its paths are
    /// (<see cref="VsixGalleryPaths"/>): <c>https://host/feeds/{slug}/vsix</c>, or for a request
    /// made through the token URL, <c>https://host/feeds/{slug}/vsix/auth/{token}</c>, so the
    /// gallery's links carry the token too.
    /// </summary>
    public static string VsixGallery(this IUrlHelper url)
    {
        var context = url.ActionContext.HttpContext;
        var request = context.Request;
        var gallery = $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}/vsix";

        return context.Items.TryGetValue(VsixGalleryTokenMiddleware.TokenItem, out var token) && token is string text
            ? $"{gallery}/auth/{Uri.EscapeDataString(text)}"
            : gallery;
    }

    /// <summary>
    /// The URL of the gallery with a personal access token in place of <paramref name="tokenPlaceholder"/>,
    /// which Visual Studio reads when the feed needs credentials.
    /// </summary>
    public static string VsixAtomFeedWithToken(this IUrlHelper url, string tokenPlaceholder)
    {
        return $"{url.VsixGallery()}/auth/{tokenPlaceholder}/{VsixGalleryPaths.AtomFeed}";
    }

    /// <summary>
    /// The absolute URL of the Atom feed that Visual Studio adds as an extension gallery.
    /// </summary>
    public static string VsixAtomFeed(this IUrlHelper url)
    {
        return $"{url.VsixGallery()}/{VsixGalleryPaths.AtomFeed}";
    }

    /// <summary>
    /// The absolute URL of the VSIX file of a version.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than from the route: routing lowercases URLs, and the file name keeps the identifier's case.
    /// </remarks>
    public static string VsixDownload(this IUrlHelper url, string id, string version)
    {
        return $"{url.VsixGallery()}/{VsixGalleryPaths.GetFilePath(id, version)}";
    }
}
