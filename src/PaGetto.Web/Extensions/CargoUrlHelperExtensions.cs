using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PaGetto.Web.Extensions;

public static class CargoUrlHelperExtensions
{
    /// <summary>
    /// The absolute URL of the sparse index of the current Cargo feed, which Cargo is configured
    /// with (after <c>sparse+</c>): <c>https://host/feeds/{slug}/cargo/index/</c>.
    /// </summary>
    public static string CargoIndex(this IUrlHelper url)
    {
        return GetBaseUrl(url.ActionContext.HttpContext.Request) + "/cargo/index/";
    }

    /// <summary>
    /// The absolute URL of the registry web API of the current Cargo feed, the <c>api</c> of its
    /// <c>config.json</c>. Cargo adds <c>/api/v1/...</c> to it.
    /// </summary>
    public static string CargoApi(this IUrlHelper url)
    {
        return GetBaseUrl(url.ActionContext.HttpContext.Request) + "/cargo";
    }

    /// <summary>
    /// The absolute URL that crates of the current Cargo feed are downloaded from, the <c>dl</c> of
    /// its <c>config.json</c>. Cargo adds <c>/{crate}/{version}/download</c> to it.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than from the route: routing lowercases URLs, and crate names keep their case.
    /// </remarks>
    public static string CargoDownloads(this IUrlHelper url)
    {
        return url.CargoApi() + "/api/v1/crates";
    }

    private static string GetBaseUrl(HttpRequest request)
    {
        return $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}";
    }
}
