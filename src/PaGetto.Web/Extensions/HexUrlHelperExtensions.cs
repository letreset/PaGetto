using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PaGetto.Web.Extensions;

public static class HexUrlHelperExtensions
{
    /// <summary>
    /// The absolute URL of the Hex repository of the current feed, which clients add with
    /// <c>mix hex.repo add</c>: <c>https://host/feeds/{slug}/hex</c>. Clients add <c>/packages/{name}</c>,
    /// <c>/tarballs/{name}-{version}.tar</c> and the like to it.
    /// </summary>
    public static string HexRepository(this IUrlHelper url)
    {
        return GetBaseUrl(url.ActionContext.HttpContext.Request) + "/hex";
    }

    /// <summary>
    /// The absolute URL of the Hex HTTP API of the current feed, which <c>mix hex.publish</c> is
    /// given as <c>HEX_API_URL</c>: <c>https://host/feeds/{slug}/hex/api</c>.
    /// </summary>
    public static string HexApi(this IUrlHelper url)
    {
        return url.HexRepository() + "/api";
    }

    private static string GetBaseUrl(HttpRequest request)
    {
        return $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}";
    }
}
