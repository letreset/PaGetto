using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Content;
using PaGetto.Core.Feeds;
using PaGetto.Protocol.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NuGet.Versioning;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The Package Content resource, used to download content from packages.
/// See: https://docs.microsoft.com/nuget/api/package-base-address-resource
/// </summary>

[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class PackageContentController : Controller
{
    private readonly IPackageContentService _content;
    private readonly IFeedContext _feedContext;

    public PackageContentController(IPackageContentService content, IFeedContext feedContext)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(feedContext);

        _content = content;
        _feedContext = feedContext;
    }

    [ContentETag]
    public async Task<ActionResult<PackageVersionsResponse>> GetPackageVersionsAsync(string id, CancellationToken cancellationToken)
    {
        var versions = await _content.GetPackageVersionsOrNullAsync(_feedContext.CurrentFeed.Id, _feedContext.CurrentFeed.Slug, id, cancellationToken);
        if (versions == null)
        {
            return NotFound();
        }

        return versions;
    }

    /// <summary>
    /// Download a specific package version.
    /// </summary>
    /// <param name="id">Package id, e.g. "PaGetto.Protocol".</param>
    /// <param name="version">Package version, e.g. "1.2.0".</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The requested package in an octet stream, or 404 not found if the package isn't found.</returns>
    public async Task<IActionResult> DownloadPackageAsync(string id, string version, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        var packageStream = await _content.GetPackageContentStreamOrNullAsync(_feedContext.CurrentFeed.Id, _feedContext.CurrentFeed.Slug, id, nugetVersion, cancellationToken);
        if (packageStream == null)
        {
            return NotFound();
        }

        return File(packageStream, "application/octet-stream");
    }

    public async Task<IActionResult> DownloadNuspecAsync(string id, string version, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        var nuspecStream = await _content.GetPackageManifestStreamOrNullAsync(_feedContext.CurrentFeed.Id, _feedContext.CurrentFeed.Slug, id, nugetVersion, cancellationToken);
        if (nuspecStream == null)
        {
            return NotFound();
        }

        return File(nuspecStream, "text/xml");
    }

    public async Task<IActionResult> DownloadReadmeAsync(string id, string version, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        var readmeStream = await _content.GetPackageReadmeStreamOrNullAsync(_feedContext.CurrentFeed.Id, _feedContext.CurrentFeed.Slug, id, nugetVersion, cancellationToken);
        if (readmeStream == null)
        {
            return NotFound();
        }

        return File(readmeStream, "text/markdown");
    }

    public async Task<IActionResult> DownloadIconAsync(string id, string version, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        var iconStream = await _content.GetPackageIconStreamOrNullAsync(_feedContext.CurrentFeed.Id, _feedContext.CurrentFeed.Slug, id, nugetVersion, cancellationToken);
        if (iconStream == null)
        {
            return NotFound();
        }

        await using var bufferedStream = new MemoryStream();
        await iconStream.CopyToAsync(bufferedStream, cancellationToken);
        var iconBytes = bufferedStream.ToArray();

        Response.Headers.CacheControl = "private, max-age=3600";

        return File(iconBytes, DetectImageContentType(iconBytes));
    }

    private static string DetectImageContentType(byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;

        if (span.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (span.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return "image/jpeg";
        }

        if (span.StartsWith("GIF87a"u8) || span.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        if (span.StartsWith("BM"u8))
        {
            return "image/bmp";
        }

        if (span.Length >= 12 && span.StartsWith("RIFF"u8) && span[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return "application/octet-stream";
    }
}
