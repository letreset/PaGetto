using Microsoft.AspNetCore.Mvc;
using PaGetto.Core.Composer;

namespace PaGetto.Web.Extensions;

public static class ComposerUrlHelperExtensions
{
    /// <summary>
    /// The absolute URL of the Composer repository of the current feed, which clients add to the
    /// <c>repositories</c> of their <c>composer.json</c>: <c>https://host/feeds/{slug}/composer</c>.
    /// </summary>
    public static string ComposerRepository(this IUrlHelper url)
    {
        var request = url.ActionContext.HttpContext.Request;
        return $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}/composer";
    }

    /// <summary>
    /// The absolute URL the zips of the current feed are under.
    /// </summary>
    public static string ComposerDists(this IUrlHelper url)
    {
        return url.ComposerRepository() + "/dists";
    }

    /// <summary>
    /// The absolute URL of the zip of a version.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than from the route: routing lowercases URLs, and a version keeps its case.
    /// </remarks>
    public static string ComposerDist(this IUrlHelper url, string name, string version, string reference)
    {
        return $"{url.ComposerDists()}/{ComposerManifest.GetDistPath(name, version, reference)}";
    }
}
