using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.VsCode;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The gallery of VS Code feeds, at <c>vscode/</c> under the feed: the gallery API that VS Code and
/// the editors built on it query once <c>product.json</c> points their <c>extensionsGallery</c> at
/// it (the API of the Visual Studio Marketplace, which Open VSX also serves), and an upload of VSIX
/// files, which the gallery API has no protocol for.
/// </summary>
[FeedType(FeedType.VsCode)]
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class VsCodeGalleryController : Controller
{
    private const long BytesPerMiB = 1024L * 1024;
    private const string TextContentType = "text/plain; charset=utf-8";

    private readonly IVsCodePackageService _packages;
    private readonly IFeedContext _feedContext;
    private readonly FeedChangeAuthorizer _changes;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly WebAuditLog _audit;

    public VsCodeGalleryController(
        IVsCodePackageService packages,
        IFeedContext feedContext,
        FeedChangeAuthorizer changes,
        IFeedSettingsResolver feedSettings,
        WebAuditLog audit)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    // POST vscode/gallery/extensionquery: a query of the gallery API.
    public async Task<IActionResult> Query([FromBody] VsCodeGalleryQuery query, CancellationToken cancellationToken)
    {
        var result = await _packages.QueryAsync(_feedContext.CurrentFeed, query ?? new VsCodeGalleryQuery(), Url.VsCodeGallery(), cancellationToken);
        return Json(result);
    }

    // GET vscode/gallery/vscode/{publisher}/{name}/latest: an extension with its latest versions.
    public async Task<IActionResult> Latest(string publisher, string name, CancellationToken cancellationToken)
    {
        var extension = await _packages.GetLatestOrNullAsync(_feedContext.CurrentFeed, $"{publisher}.{name}", Url.VsCodeGallery(), cancellationToken);
        return extension == null ? NotFound() : Json(extension);
    }

    // GET vscode/asset/{publisher}/{name}/{version}/{assetType}?targetPlatform=: a file of a version.
    public async Task<IActionResult> Asset(string publisher, string name, string version, string assetType, [FromQuery] string targetPlatform, CancellationToken cancellationToken)
    {
        var asset = await _packages.GetAssetOrNullAsync(_feedContext.CurrentFeed, $"{publisher}.{name}", version, targetPlatform, assetType, cancellationToken);
        if (asset == null)
        {
            return NotFound();
        }

        return asset.FileName == null
            ? File(asset.Content, asset.ContentType)
            : File(asset.Content, asset.ContentType, asset.FileName);
    }

    // GET vscode/item?itemName={publisher}.{name}: the extension's page, which the editors link to.
    public IActionResult Item([FromQuery] string itemName)
    {
        if (!VsCodeExtensionId.TryParse(itemName, out _, out _))
        {
            return NotFound();
        }

        return Redirect(Url.Page("/Artifact", new { id = VsCodeExtensionId.Normalize(itemName) }));
    }

    // PUT or POST vscode/upload: uploads a VSIX file as the body, e.g. with curl -T.
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;

        var denied = await CheckCanPublishAsync(cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        // The server-wide request limit applies before the feed is known; a feed can only lower it.
        var maxBytes = _feedSettings.GetMaxPackageSizeBytes(feed);
        if (Request.ContentLength > maxBytes)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", null, null);
            return TooLarge(maxBytes);
        }

        var sizeLimit = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = maxBytes;
        }

        VsCodeUploadResult result;
        try
        {
            result = await _packages.UploadAsync(feed, Request.Body, maxBytes, cancellationToken);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", null, null);
            return TooLarge(maxBytes);
        }

        switch (result.Status)
        {
            case VsCodeUploadStatus.Success:
                await AuditAsync(LogLevel.Information, "package_upload_succeeded", result.Id, result.Version);
                return StatusCode(StatusCodes.Status201Created);

            case VsCodeUploadStatus.AlreadyExists:
                await AuditAsync(LogLevel.Warning, "package_upload_already_exists", result.Id, result.Version);
                return Text(StatusCodes.Status409Conflict, result.Message);

            case VsCodeUploadStatus.TooLarge:
                await AuditAsync(LogLevel.Warning, "package_upload_too_large", result.Id, result.Version);
                return Text(StatusCodes.Status413PayloadTooLarge, result.Message);

            default:
                await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", result.Id, result.Version);
                return Text(StatusCodes.Status400BadRequest, result.Message);
        }
    }

    /// <summary>
    /// Returns null when the caller may publish to the current feed, or the response to send
    /// otherwise. The caller is already known to be allowed to read the feed.
    /// </summary>
    private async Task<IActionResult> CheckCanPublishAsync(CancellationToken cancellationToken)
    {
        var access = await _changes.CheckAsync(User, Request.GetApiKeyOrPassword(), FeedPermissionRequirement.Push, cancellationToken);
        if (access == FeedChangeAccess.Unauthorized)
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Challenge(AuthenticationConstants.NugetBasicAuthenticationScheme);
            }

            await AuditAsync(LogLevel.Warning, "package_upload_unauthorized", null, null);
            return Text(StatusCodes.Status403Forbidden, "You do not have permission to upload extensions to this feed.");
        }

        if (access == FeedChangeAccess.ReadOnly)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_read_only", null, null);
            return Text(StatusCodes.Status403Forbidden, "This feed is read-only.");
        }

        return null;
    }

    private static ContentResult TooLarge(long maxBytes)
    {
        return Text(StatusCodes.Status413PayloadTooLarge, $"The extension is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.");
    }

    private static ContentResult Text(int statusCode, string message)
    {
        return new ContentResult
        {
            StatusCode = statusCode,
            ContentType = TextContentType,
            Content = message,
        };
    }

    private async Task AuditAsync(LogLevel level, string eventName, string packageName, string packageVersion)
    {
        await _audit.PackageApiAsync(HttpContext, level, eventName, _feedContext.CurrentFeed.Slug, packageName, packageVersion, Request.GetApiKeyOrPassword());
    }
}
