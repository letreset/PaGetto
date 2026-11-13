using System;
using Microsoft.AspNetCore.Mvc;

namespace PaGetto.Web.Extensions;

public static class RubyGemsUrlHelperExtensions
{
    /// <summary>
    /// The absolute URL of the gem server of the current RubyGems feed, which Bundler and RubyGems
    /// use as the gem source: <c>https://host/feeds/{slug}/rubygems/</c>.
    /// </summary>
    public static string RubyGemsSource(this IUrlHelper url)
    {
        return url.RubyGemsHost() + "/";
    }

    /// <summary>
    /// The gem source without its trailing slash, which <c>gem push --host</c> takes: it adds
    /// <c>/api/v1/gems</c> itself.
    /// </summary>
    public static string RubyGemsHost(this IUrlHelper url)
    {
        var request = url.ActionContext.HttpContext.Request;
        return $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}/rubygems";
    }

    /// <summary>
    /// The absolute URL of a gem file.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than from the route: routing lowercases URLs, and gem names keep their case.
    /// </remarks>
    public static string RubyGemsGem(this IUrlHelper url, string fileName)
    {
        return $"{url.RubyGemsSource()}gems/{Uri.EscapeDataString(fileName)}";
    }
}
