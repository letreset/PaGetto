using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PaGetto.Web.Extensions;

public static class PyPIUrlHelperExtensions
{
    /// <summary>
    /// The absolute URL of the simple index of the current PyPI feed, which pip and uv are
    /// configured with: <c>https://host/feeds/{slug}/pypi/simple/</c>.
    /// </summary>
    public static string PyPISimpleIndex(this IUrlHelper url)
    {
        return GetBaseUrl(url.ActionContext.HttpContext.Request) + "/pypi/simple/";
    }

    /// <summary>
    /// The absolute URL of a project's page of the simple index of the current PyPI feed.
    /// </summary>
    /// <param name="url">The URL helper.</param>
    /// <param name="name">The normalized project name.</param>
    public static string PyPIProject(this IUrlHelper url, string name)
    {
        return url.PyPISimpleIndex() + Uri.EscapeDataString(name) + "/";
    }

    /// <summary>
    /// The absolute URL of a file of a version on the current PyPI feed.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than from the route: routing lowercases URLs, and file names keep their case.
    /// </remarks>
    /// <param name="url">The URL helper.</param>
    /// <param name="name">The normalized project name.</param>
    /// <param name="version">The normalized version.</param>
    /// <param name="fileName">The file name.</param>
    public static string PyPIFile(this IUrlHelper url, string name, string version, string fileName)
    {
        return GetBaseUrl(url.ActionContext.HttpContext.Request)
            + $"/pypi/files/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(fileName)}";
    }

    /// <summary>
    /// The absolute URL that <c>twine</c> and <c>uv publish</c> upload to on the current PyPI feed.
    /// </summary>
    public static string PyPIUpload(this IUrlHelper url)
    {
        return GetBaseUrl(url.ActionContext.HttpContext.Request) + "/pypi/legacy/";
    }

    private static string GetBaseUrl(HttpRequest request)
    {
        return $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}";
    }
}
