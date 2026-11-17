using Microsoft.AspNetCore.Mvc;
using PaGetto.Core.Cran;

namespace PaGetto.Web.Extensions;

public static class CranUrlHelperExtensions
{
    /// <summary>
    /// The absolute URL of the CRAN repository of the current feed, which clients pass to
    /// <c>install.packages</c> as <c>repos</c>: <c>https://host/feeds/{slug}/cran</c>.
    /// </summary>
    public static string CranRepository(this IUrlHelper url)
    {
        var request = url.ActionContext.HttpContext.Request;
        return $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}/cran";
    }

    /// <summary>
    /// The absolute URL of the source package of a version.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than from the route: routing lowercases URLs, and R matches names by case.
    /// </remarks>
    public static string CranPackage(this IUrlHelper url, string name, string version)
    {
        return $"{url.CranRepository()}/src/contrib/{CranFileName.Create(name, version)}";
    }
}
