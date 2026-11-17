using System;
using System.IO;
using System.IO.Compression;
using System.Net;
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
using PaGetto.Core.Cran;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The CRAN repository of CRAN feeds, at <c>cran/</c> under the feed: the repository layout of
/// source packages, which <c>install.packages</c> and renv read, and an upload of source packages,
/// which R has no protocol for. The upload takes a <c>PUT</c> of the package to its path, as Nexus
/// Repository does. Binary packages are not hosted: the indexes of the binary trees are empty, so
/// R installs the source package.
/// See: https://cran.r-project.org/doc/manuals/r-release/R-admin.html#Setting-up-a-package-repository
/// </summary>
[FeedType(FeedType.Cran)]
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class CranRepositoryController : Controller
{
    private const long BytesPerMiB = 1024L * 1024;

    private const string IndexFileName = "PACKAGES";
    private const string GzipIndexFileName = "PACKAGES.gz";
    private const string TextContentType = "text/plain; charset=utf-8";
    private const string GzipContentType = "application/x-gzip";

    private readonly ICranPackageService _packages;
    private readonly IFeedContext _feedContext;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IAuthenticationService _authentication;
    private readonly IPermissionService _permissionService;
    private readonly IOptionsSnapshot<PaGettoOptions> _options;
    private readonly WebAuditLog _audit;

    public CranRepositoryController(
        ICranPackageService packages,
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

    // GET cran/src/contrib/{file}: PACKAGES, PACKAGES.gz or a source package.
    // PACKAGES.rds, which R asks for first, is not served: R then reads PACKAGES.gz.
    public async Task<IActionResult> SourceFile(string file, CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;

        if (file is IndexFileName or GzipIndexFileName)
        {
            var index = await _packages.GetIndexAsync(feed, cancellationToken);
            return IndexFile(file, index);
        }

        var package = await _packages.GetPackageStreamOrNullAsync(feed, file, cancellationToken);
        return package == null ? NotFound() : File(package, GzipContentType);
    }

    // GET cran/src/contrib/Archive/{name}/: the directory listing of a package's older versions,
    // which mirrors of this feed read.
    public async Task<IActionResult> ArchiveListing(string name, CancellationToken cancellationToken)
    {
        var files = await _packages.ListArchiveOrNullAsync(_feedContext.CurrentFeed, name, cancellationToken);
        if (files == null)
        {
            return NotFound();
        }

        var title = WebUtility.HtmlEncode($"Index of Archive/{name}");
        var html = new StringBuilder($"<!DOCTYPE html>\n<html>\n<head><title>{title}</title></head>\n<body>\n<h1>{title}</h1>\n<ul>\n");
        foreach (var fileName in files)
        {
            var encoded = WebUtility.HtmlEncode(fileName);
            html.Append($"<li><a href=\"{encoded}\">{encoded}</a></li>\n");
        }

        html.Append("</ul>\n</body>\n</html>\n");

        return Content(html.ToString(), "text/html; charset=utf-8");
    }

    // GET cran/src/contrib/Archive/{name}/{file}: an older version, where renv looks for it.
    public async Task<IActionResult> ArchiveFile(string name, string file, CancellationToken cancellationToken)
    {
        if (!CranFileName.TryParse(file, out var fileName, out _) || !string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        var package = await _packages.GetPackageStreamOrNullAsync(_feedContext.CurrentFeed, file, cancellationToken);
        return package == null ? NotFound() : File(package, GzipContentType);
    }

    // GET cran/bin/{**path}: the trees of binary packages, which are not hosted. Their indexes are
    // empty, so R on Windows and macOS installs source packages without a warning.
    public IActionResult BinaryFile(string path)
    {
        var file = path?[(path.LastIndexOf('/') + 1)..];
        return file is IndexFileName or GzipIndexFileName ? IndexFile(file, "") : NotFound();
    }

    // PUT cran/src/contrib/{file}: uploads a source package, e.g. with curl -T.
    public async Task<IActionResult> Upload(string file, CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;
        string name = null;
        CranVersion version = null;
        if (CranFileName.TryParse(file, out var fileName, out var fileVersion))
        {
            name = fileName;
            version = fileVersion;
        }

        var denied = await CheckCanPublishAsync(name ?? file, version?.Text, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        // The server-wide request limit applies before the feed is known; a feed can only lower it.
        var maxBytes = _feedSettings.GetMaxPackageSizeMiB(feed) * BytesPerMiB;
        if (Request.ContentLength > maxBytes)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", name ?? file, version?.Text);
            return TooLarge(maxBytes);
        }

        var sizeLimit = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = maxBytes;
        }

        CranUploadResult result;
        try
        {
            result = await _packages.UploadAsync(feed, file, Request.Body, maxBytes, cancellationToken);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", name ?? file, version?.Text);
            return TooLarge(maxBytes);
        }

        var packageName = result.Name ?? name ?? file;
        var packageVersion = result.Version ?? version?.Text;
        switch (result.Status)
        {
            case CranUploadStatus.Success:
                await AuditAsync(LogLevel.Information, "package_upload_succeeded", packageName, packageVersion);
                return StatusCode(StatusCodes.Status201Created);

            case CranUploadStatus.AlreadyExists:
                await AuditAsync(LogLevel.Warning, "package_upload_already_exists", packageName, packageVersion);
                return Text(StatusCodes.Status409Conflict, result.Message);

            case CranUploadStatus.TooLarge:
                await AuditAsync(LogLevel.Warning, "package_upload_too_large", packageName, packageVersion);
                return Text(StatusCodes.Status413PayloadTooLarge, result.Message);

            default:
                await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", packageName, packageVersion);
                return Text(StatusCodes.Status400BadRequest, result.Message);
        }
    }

    private FileContentResult IndexFile(string file, string index)
    {
        var bytes = Encoding.UTF8.GetBytes(index);
        if (file == IndexFileName)
        {
            return File(bytes, TextContentType);
        }

        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return File(compressed.ToArray(), GzipContentType);
    }

    /// <summary>
    /// Returns null when the caller may publish to the current feed, or the response to send
    /// otherwise. The caller is already known to be allowed to read the feed.
    /// </summary>
    private async Task<IActionResult> CheckCanPublishAsync(string name, string version, CancellationToken cancellationToken)
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

            await AuditAsync(LogLevel.Warning, "package_upload_unauthorized", name, version);
            return Text(StatusCodes.Status403Forbidden, "You do not have permission to upload packages to this feed.");
        }

        if (_feedSettings.GetIsReadOnlyMode(feed))
        {
            await AuditAsync(LogLevel.Warning, "package_upload_read_only", name, version);
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
        return Text(StatusCodes.Status413PayloadTooLarge, $"The package is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.");
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
