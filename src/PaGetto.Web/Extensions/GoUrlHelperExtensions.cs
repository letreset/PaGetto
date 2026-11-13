using Microsoft.AspNetCore.Mvc;
using PaGetto.Core.Go;

namespace PaGetto.Web.Extensions;

public static class GoUrlHelperExtensions
{
    /// <summary>
    /// The absolute URL of the module proxy of the current Go feed, which <c>GOPROXY</c> is set to:
    /// <c>https://host/feeds/{slug}/go</c>.
    /// </summary>
    public static string GoProxy(this IUrlHelper url)
    {
        var request = url.ActionContext.HttpContext.Request;
        return $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}/go";
    }

    /// <summary>
    /// The absolute URL of the zip of a module version, which is also the URL it is uploaded to.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than from the route: routing lowercases URLs, and the escaped path keeps
    /// the case of the module path in its <c>!</c> escapes.
    /// </remarks>
    public static string GoZip(this IUrlHelper url, string modulePath, string version)
    {
        return $"{url.GoProxy()}/{GoModulePath.Escape(modulePath)}/@v/{GoModulePath.Escape(version)}.zip";
    }
}
