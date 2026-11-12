using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using PaGetto.Core.Npm;

namespace PaGetto.Web.Extensions;

public static class NpmUrlHelperExtensions
{
    /// <summary>
    /// The URL of a version's tarball on the current npm feed, in the form the npm registry uses:
    /// <c>npm/@scope/name/-/name-1.0.0.tgz</c>.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than from the route: routing lowercases URLs, and an older package
    /// such as JSONStream must be asked for by its own name when it is mirrored.
    /// </remarks>
    /// <param name="url">The URL helper.</param>
    /// <param name="name">The package name.</param>
    /// <param name="version">The version.</param>
    /// <param name="protocol">The scheme of an absolute URL, or null for a path.</param>
    public static string NpmTarball(this IUrlHelper url, string name, string version, string protocol = null)
    {
        var request = url.ActionContext.HttpContext.Request;

        // Each part of a scoped name is a path segment, and the '@' stays readable.
        var namePath = string.Join('/', name.Split('/').Select(segment => Uri.EscapeDataString(segment).Replace("%40", "@", StringComparison.Ordinal)));
        var fileName = Uri.EscapeDataString(NpmPackageName.GetTarballFileName(name, version));
        var path = $"{request.PathBase.ToUriComponent()}/npm/{namePath}/-/{fileName}";

        return protocol == null ? path : $"{protocol}://{request.Host.ToUriComponent()}{path}";
    }
}
