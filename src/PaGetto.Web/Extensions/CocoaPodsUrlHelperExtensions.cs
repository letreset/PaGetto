using System;
using Microsoft.AspNetCore.Mvc;
using PaGetto.Core.CocoaPods;

namespace PaGetto.Web.Extensions;

public static class CocoaPodsUrlHelperExtensions
{
    /// <summary>
    /// The absolute URL of the spec repository of the current feed, which clients name as a
    /// <c>source</c> in their Podfile: <c>https://host/feeds/{slug}/cocoapods/</c>.
    /// </summary>
    public static string CocoaPodsRepository(this IUrlHelper url)
    {
        var request = url.ActionContext.HttpContext.Request;
        return $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}/cocoapods/";
    }

    /// <summary>
    /// The absolute URL the source archives of the current feed are under, without a trailing slash.
    /// </summary>
    public static string CocoaPodsSources(this IUrlHelper url)
    {
        return url.CocoaPodsRepository() + "sources";
    }

    /// <summary>
    /// The absolute URL of the source archive of a version.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than from the route: routing lowercases URLs, and pod names are case-sensitive.
    /// </remarks>
    public static string CocoaPodsSource(this IUrlHelper url, string name, string version, string type)
    {
        var fileName = CocoaPodsPodspec.GetArchiveFileName(name, version, type);
        return $"{url.CocoaPodsSources()}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(fileName)}";
    }
}
