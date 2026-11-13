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
using PaGetto.Core.Go;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The module proxy of Go feeds, at <c>go/</c> under the feed: the GOPROXY protocol, which the go
/// command reads, and an upload of module zips, which Go has no protocol for.
/// See: https://go.dev/ref/mod#goproxy-protocol
/// </summary>
[FeedType(FeedType.Go)]
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class GoProxyController : Controller
{
    private const long BytesPerMiB = 1024L * 1024;

    private const string TextContentType = "text/plain; charset=utf-8";

    private readonly IGoModuleService _modules;
    private readonly IFeedContext _feedContext;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IAuthenticationService _authentication;
    private readonly IPermissionService _permissionService;
    private readonly IOptionsSnapshot<PaGettoOptions> _options;
    private readonly WebAuditLog _audit;

    public GoProxyController(
        IGoModuleService modules,
        IFeedContext feedContext,
        IFeedSettingsResolver feedSettings,
        IAuthenticationService authentication,
        IPermissionService permissionService,
        IOptionsSnapshot<PaGettoOptions> options,
        WebAuditLog audit)
    {
        _modules = modules ?? throw new ArgumentNullException(nameof(modules));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    // GET go/{module}/@v/list, go/{module}/@latest, go/{module}/@v/{version}.info, .mod and .zip
    public async Task<IActionResult> Get(string path, CancellationToken cancellationToken)
    {
        if (!GoProxyPath.TryParse(path, out var request))
        {
            return NotFoundText($"not found: {path}");
        }

        var feed = _feedContext.CurrentFeed;
        var module = request.ModulePath;
        var version = request.Version;

        switch (request.Request)
        {
            case GoProxyRequest.List:
                var versions = await _modules.ListVersionsOrNullAsync(feed, module, cancellationToken);
                if (versions == null)
                {
                    return NotFoundText($"not found: module {module}");
                }

                var list = new StringBuilder();
                foreach (var listed in versions)
                {
                    list.Append(listed).Append('\n');
                }

                return Content(list.ToString(), TextContentType);

            case GoProxyRequest.Latest:
                var latest = await _modules.GetLatestInfoOrNullAsync(feed, module, cancellationToken);
                return latest == null
                    ? NotFoundText($"not found: module {module}")
                    : Content(latest, "application/json");

            case GoProxyRequest.Info:
                var info = await _modules.GetInfoOrNullAsync(feed, module, version, cancellationToken);
                return info == null
                    ? NotFoundText($"not found: {module}@{version}")
                    : Content(info, "application/json");

            case GoProxyRequest.Mod:
                var mod = await _modules.GetModOrNullAsync(feed, module, version, cancellationToken);
                return mod == null
                    ? NotFoundText($"not found: {module}@{version}")
                    : Content(mod, TextContentType);

            default:
                var zip = await _modules.GetZipStreamOrNullAsync(feed, module, version, cancellationToken);
                return zip == null
                    ? NotFoundText($"not found: {module}@{version}")
                    : File(zip, "application/zip");
        }
    }

    // PUT go/{module}/@v/{version}.zip
    // The go command has no upload; this takes the zip as the body, e.g. from curl -T.
    public async Task<IActionResult> Upload(string path, CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;

        if (!GoProxyPath.TryParse(path, out var request) || request.Request != GoProxyRequest.Zip)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", null, null);
            return Text(StatusCodes.Status400BadRequest, "Upload a module zip to {module}/@v/{version}.zip, with the module path and version escaped.");
        }

        var module = request.ModulePath;
        var version = request.Version;

        var denied = await CheckCanPublishAsync(module, version, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        // The server-wide request limit applies before the feed is known; a feed can only lower it.
        var maxBytes = _feedSettings.GetMaxPackageSizeMiB(feed) * BytesPerMiB;
        if (Request.ContentLength > maxBytes)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", module, version);
            return TooLarge(maxBytes);
        }

        var sizeLimit = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = maxBytes;
        }

        GoUploadResult result;
        try
        {
            result = await _modules.UploadAsync(feed, module, version, Request.Body, maxBytes, cancellationToken);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", module, version);
            return TooLarge(maxBytes);
        }

        switch (result.Status)
        {
            case GoUploadStatus.Success:
                await AuditAsync(LogLevel.Information, "package_upload_succeeded", module, version);
                return StatusCode(StatusCodes.Status201Created);

            case GoUploadStatus.AlreadyExists:
                await AuditAsync(LogLevel.Warning, "package_upload_already_exists", module, version);
                return Text(StatusCodes.Status409Conflict, result.Message);

            case GoUploadStatus.TooLarge:
                await AuditAsync(LogLevel.Warning, "package_upload_too_large", module, version);
                return Text(StatusCodes.Status413PayloadTooLarge, result.Message);

            default:
                await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", module, version);
                return Text(StatusCodes.Status400BadRequest, result.Message);
        }
    }

    /// <summary>
    /// Returns null when the caller may publish to the current feed, or the response to send
    /// otherwise. The caller is already known to be allowed to read the feed.
    /// </summary>
    private async Task<IActionResult> CheckCanPublishAsync(string module, string version, CancellationToken cancellationToken)
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

            await AuditAsync(LogLevel.Warning, "package_upload_unauthorized", module, version);
            return Text(StatusCodes.Status403Forbidden, "You do not have permission to upload modules to this feed.");
        }

        if (_feedSettings.GetIsReadOnlyMode(feed))
        {
            await AuditAsync(LogLevel.Warning, "package_upload_read_only", module, version);
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
        return Text(StatusCodes.Status413PayloadTooLarge, $"The zip is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.");
    }

    /// <summary>
    /// A 404, which tells the go command to try the next proxy of its GOPROXY list. It shows the text.
    /// </summary>
    private static ContentResult NotFoundText(string message)
    {
        return Text(StatusCodes.Status404NotFound, message);
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
