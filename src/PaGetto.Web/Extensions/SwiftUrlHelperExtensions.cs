using System;
using Microsoft.AspNetCore.Mvc;

namespace PaGetto.Web.Extensions;

public static class SwiftUrlHelperExtensions
{
    /// <summary>
    /// The absolute URL of the Swift package registry of the current feed, which clients set with
    /// <c>swift package-registry set</c>: <c>https://host/feeds/{slug}/swift</c>.
    /// </summary>
    public static string SwiftRegistry(this IUrlHelper url)
    {
        var request = url.ActionContext.HttpContext.Request;
        return $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}/swift";
    }

    /// <summary>
    /// The absolute URL of a release, or of the release list of a package when the version is null.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than from the route: routing lowercases URLs, and an identifier keeps its case.
    /// </remarks>
    public static string SwiftRelease(this IUrlHelper url, string scope, string name, string version)
    {
        var package = $"{url.SwiftRegistry()}/{Uri.EscapeDataString(scope)}/{Uri.EscapeDataString(name)}";
        return version == null ? package : $"{package}/{Uri.EscapeDataString(version)}";
    }

    /// <summary>
    /// The absolute URL of the source archive of a release.
    /// </summary>
    public static string SwiftArchive(this IUrlHelper url, string scope, string name, string version)
    {
        return url.SwiftRelease(scope, name, version) + ".zip";
    }
}
