using System;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Vsix;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The gallery of VSIX feeds, at <c>vsix/</c> under the feed: the Atom feed of a private gallery,
/// which Visual Studio adds under Tools &gt; Options &gt; Environment &gt; Extensions, the VSIX files
/// and images it links to, and an upload of VSIX files, which Visual Studio has no protocol for.
/// See: https://learn.microsoft.com/visualstudio/extensibility/private-galleries
/// </summary>
[FeedType(FeedType.Vsix)]
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class VsixGalleryController : Controller
{
    private const long BytesPerMiB = 1024L * 1024;

    private const string TextContentType = "text/plain; charset=utf-8";
    private const string VsixContentType = "application/octet-stream";
    private const string VsixExtension = ".vsix";

    private readonly IVsixPackageService _packages;
    private readonly IFeedContext _feedContext;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IAuthenticationService _authentication;
    private readonly IPermissionService _permissionService;
    private readonly IOptionsSnapshot<PaGettoOptions> _options;
    private readonly WebAuditLog _audit;

    public VsixGalleryController(
        IVsixPackageService packages,
        IFeedContext feedContext,
        IFeedSettingsResolver feedSettings,
        IAuthenticationService authentication,
        IPermissionService permissionService,
        IOptionsSnapshot<PaGettoOptions> options,
        WebAuditLog audit)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    // GET vsix/atom.xml: the gallery.
    public async Task<IActionResult> AtomFeed(CancellationToken cancellationToken)
    {
        var feed = await _packages.GetAtomFeedAsync(_feedContext.CurrentFeed, Url.VsixGallery(), cancellationToken);

        return Content(feed, VsixAtomFeed.ContentType);
    }

    // GET vsix/extensions/{id}/{version}/{file}: the VSIX file of a version.
    public async Task<IActionResult> Download(string id, string version, string file, CancellationToken cancellationToken)
    {
        if (file == null || !file.EndsWith(VsixExtension, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        var package = await _packages.GetPackageStreamOrNullAsync(_feedContext.CurrentFeed, id, version, cancellationToken);
        return package == null ? NotFound() : File(package, VsixContentType, file);
    }

    // GET vsix/extensions/{id}/{version}/icon and /preview: the images the gallery links to.
    public async Task<IActionResult> Image(string id, string version, VsixImageKind kind, CancellationToken cancellationToken)
    {
        var image = await _packages.GetImageOrNullAsync(_feedContext.CurrentFeed, id, version, kind, cancellationToken);
        if (image == null)
        {
            return NotFound();
        }

        // A version's images only change when the version is replaced.
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(image.Content, image.ContentType);
    }

    // PUT or POST vsix/upload: uploads a VSIX file as the body, e.g. with curl -T.
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;

        var denied = await CheckCanPublishAsync(cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        // The server-wide request limit applies before the feed is known; a feed can only lower it.
        var maxBytes = _feedSettings.GetMaxPackageSizeMiB(feed) * BytesPerMiB;
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

        VsixUploadResult result;
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
            case VsixUploadStatus.Success:
                await AuditAsync(LogLevel.Information, "package_upload_succeeded", result.Id, result.Version);
                return StatusCode(StatusCodes.Status201Created);

            case VsixUploadStatus.AlreadyExists:
                await AuditAsync(LogLevel.Warning, "package_upload_already_exists", result.Id, result.Version);
                return Text(StatusCodes.Status409Conflict, result.Message);

            case VsixUploadStatus.TooLarge:
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
        var feed = _feedContext.CurrentFeed;
        var authMode = _options.Value.Authentication?.Mode ?? AuthenticationMode.Legacy;

        bool authorized;
        if (authMode == AuthenticationMode.Legacy)
        {
            // Static auth mode: the configured API key, sent as the password.
            authorized = await _authentication.AuthenticateAsync(GetApiKey(), cancellationToken);
        }
        else
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            authorized = Guid.TryParse(userIdClaim, out var userId)
                && await _permissionService.CanPushAsync(userId, feed.Id, cancellationToken);
        }

        if (!authorized)
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Challenge(AuthenticationConstants.NugetBasicAuthenticationScheme);
            }

            await AuditAsync(LogLevel.Warning, "package_upload_unauthorized", null, null);
            return Text(StatusCodes.Status403Forbidden, "You do not have permission to upload extensions to this feed.");
        }

        if (_feedSettings.GetIsReadOnlyMode(feed))
        {
            await AuditAsync(LogLevel.Warning, "package_upload_read_only", null, null);
            return Text(StatusCodes.Status403Forbidden, "This feed is read-only.");
        }

        return null;
    }

    /// <summary>
    /// The API key in Legacy mode: the password of Basic authentication, a Bearer token, or the
    /// NuGet API key header.
    /// </summary>
    private string GetApiKey()
    {
        var apiKey = Request.Headers[HttpRequestExtensions.ApiKeyHeader].ToString();
        if (!string.IsNullOrEmpty(apiKey))
        {
            return apiKey;
        }

        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header) || header.Parameter == null)
        {
            return null;
        }

        if (string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return header.Parameter;
        }

        try
        {
            var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter)).Split(':', 2);
            return credentials.Length == 2 ? credentials[1] : null;
        }
        catch (FormatException)
        {
            return null;
        }
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
        var actor = User.Identity?.Name;
        if (string.IsNullOrEmpty(actor))
        {
            // Config mode API keys are shared and carry no user identity.
            actor = string.IsNullOrEmpty(GetApiKey()) ? "anonymous" : "api-key";
        }

        await _audit.PackageAsync(HttpContext, level, eventName, _feedContext.CurrentFeed.Slug, packageName, packageVersion, actor);
    }
}
