using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.RubyGems;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The gem server of RubyGems feeds, at <c>rubygems/</c> under the feed: the compact index, which
/// Bundler and RubyGems read, the gem files and the gemspecs <c>gem install</c> reads, and the API
/// of <c>gem push</c> and <c>gem yank</c>.
/// See: https://guides.rubygems.org/rubygems-org-compact-index-api/ and https://guides.rubygems.org/rubygems-org-api/
/// </summary>
[FeedType(FeedType.RubyGems)]
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class RubyGemsController : Controller
{
    private const long BytesPerMiB = 1024L * 1024;

    private const string TextContentType = "text/plain; charset=utf-8";
    private const string BinaryContentType = "application/octet-stream";

    private readonly IRubyGemsPackageService _gems;
    private readonly IFeedContext _feedContext;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IAuthenticationService _authentication;
    private readonly IPermissionService _permissionService;
    private readonly IOptionsSnapshot<PaGettoOptions> _options;
    private readonly WebAuditLog _audit;

    public RubyGemsController(
        IRubyGemsPackageService gems,
        IFeedContext feedContext,
        IFeedSettingsResolver feedSettings,
        IAuthenticationService authentication,
        IPermissionService permissionService,
        IOptionsSnapshot<PaGettoOptions> options,
        WebAuditLog audit)
    {
        _gems = gems ?? throw new ArgumentNullException(nameof(gems));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    // GET rubygems/versions
    public async Task<IActionResult> Versions(CancellationToken cancellationToken)
    {
        return IndexFile(await _gems.GetVersionsFileAsync(_feedContext.CurrentFeed, cancellationToken));
    }

    // GET rubygems/info/{name}
    public async Task<IActionResult> Info(string name, CancellationToken cancellationToken)
    {
        var file = await _gems.GetInfoFileOrNullAsync(_feedContext.CurrentFeed, name, cancellationToken);
        return file == null ? Text(StatusCodes.Status404NotFound, $"This feed has no gem named {name}.") : IndexFile(file);
    }

    // GET rubygems/names
    public async Task<IActionResult> Names(CancellationToken cancellationToken)
    {
        return IndexFile(await _gems.GetNamesFileAsync(_feedContext.CurrentFeed, cancellationToken));
    }

    // GET rubygems/gems/{name}-{version}[-{platform}].gem
    public async Task<IActionResult> Download(string fileName, CancellationToken cancellationToken)
    {
        var fullName = RubyGemsFileName.TrimExtension(fileName, RubyGemsFileName.GemExtension);
        var gem = fullName == null
            ? null
            : await _gems.GetGemStreamOrNullAsync(_feedContext.CurrentFeed, fullName, countDownload: !HttpMethods.IsHead(Request.Method), cancellationToken);

        return gem == null
            ? Text(StatusCodes.Status404NotFound, $"This feed has no gem file {fileName}.")
            : File(gem, BinaryContentType);
    }

    // GET rubygems/quick/Marshal.4.8/{name}-{version}[-{platform}].gemspec.rz
    public async Task<IActionResult> QuickSpec(string fileName, CancellationToken cancellationToken)
    {
        var fullName = RubyGemsFileName.TrimExtension(fileName, RubyGemsFileName.QuickSpecExtension);
        var spec = fullName == null ? null : await _gems.GetQuickSpecOrNullAsync(_feedContext.CurrentFeed, fullName, cancellationToken);

        return spec == null
            ? Text(StatusCodes.Status404NotFound, $"This feed has no gemspec {fileName}.")
            : File(spec, BinaryContentType);
    }

    // POST rubygems/api/v1/gems
    // gem push: the .gem file is the body, the API key the whole Authorization header.
    public async Task<IActionResult> Push(CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;

        var denied = await CheckCanChangeAsync(FeedPermissionRequirement.Push, "package_upload", null, null, cancellationToken);
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

        RubyGemsPushResult result;
        try
        {
            result = await _gems.PushAsync(feed, Request.Body, maxBytes, cancellationToken);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", null, null);
            return TooLarge(maxBytes);
        }

        switch (result.Status)
        {
            case RubyGemsPushStatus.Success:
                await AuditAsync(LogLevel.Information, "package_upload_succeeded", result.Name, result.Version);
                return Text(StatusCodes.Status200OK, $"Successfully registered gem: {result.Name} ({result.FullName[(result.Name.Length + 1)..]})");

            case RubyGemsPushStatus.Conflict:
                await AuditAsync(LogLevel.Warning, "package_upload_already_exists", result.Name, result.Version);
                return Text(StatusCodes.Status409Conflict, result.Message);

            case RubyGemsPushStatus.TooLarge:
                await AuditAsync(LogLevel.Warning, "package_upload_too_large", null, null);
                return TooLarge(maxBytes);

            default:
                await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", result.Name, result.Version);
                return Text(StatusCodes.Status422UnprocessableEntity, result.Message);
        }
    }

    // DELETE rubygems/api/v1/gems/yank
    // gem yank: the gem_name, version and platform are form fields.
    public async Task<IActionResult> Yank(CancellationToken cancellationToken)
    {
        var form = Request.HasFormContentType ? await Request.ReadFormAsync(cancellationToken) : null;
        string Field(string key) => form != null && form.TryGetValue(key, out var value) ? value.ToString() : Request.Query[key].ToString();

        var name = Field("gem_name");
        var version = Field("version");
        var platform = Field("platform");

        // Yanking is unlisting, which takes the delete permission, as on the package page.
        var denied = await CheckCanChangeAsync(FeedPermissionRequirement.Delete, "package_unlist", name, version, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        if (!await _gems.YankAsync(_feedContext.CurrentFeed, name, version, string.IsNullOrEmpty(platform) ? null : platform, cancellationToken))
        {
            await AuditAsync(LogLevel.Warning, "package_unlist_not_found", name, version);
            return Text(StatusCodes.Status404NotFound, $"This feed has no version {version} of gem {name}.");
        }

        await AuditAsync(LogLevel.Information, "package_unlist_succeeded", name, version);
        return Text(StatusCodes.Status200OK, $"Successfully yanked gem: {name} ({version})");
    }

    /// <summary>
    /// A file of the compact index. Its ETag is its MD5, which is what Bundler checks it against,
    /// and its <c>Repr-Digest</c> its SHA-256. Bundler asks for the bytes it doesn't have yet with
    /// a range, and checks the result against the digest.
    /// </summary>
    private FileContentResult IndexFile(byte[] content)
    {
        Response.Headers["Repr-Digest"] = $"sha-256=:{Convert.ToBase64String(SHA256.HashData(content))}:";

        return File(
            content,
            TextContentType,
            lastModified: null,
            entityTag: new EntityTagHeaderValue($"\"{RubyGemsCompactIndex.Md5(content)}\""),
            enableRangeProcessing: true);
    }

    /// <summary>
    /// Returns null when the caller may change the current feed with the given permission, or the
    /// response to send otherwise. The caller is already known to be allowed to read the feed.
    /// </summary>
    private async Task<IActionResult> CheckCanChangeAsync(
        string permission,
        string action,
        string packageName,
        string packageVersion,
        CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;
        var authMode = _options.Value.Authentication?.Mode ?? AuthenticationMode.Legacy;

        bool authorized;
        if (authMode == AuthenticationMode.Legacy)
        {
            // Static auth mode: the configured API key, which gem sends as the Authorization header.
            authorized = await _authentication.AuthenticateAsync(GetApiKey(), cancellationToken);
        }
        else
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            authorized = Guid.TryParse(userIdClaim, out var userId)
                && (permission == FeedPermissionRequirement.Push
                    ? await _permissionService.CanPushAsync(userId, feed.Id, cancellationToken)
                    : await _permissionService.CanDeleteAsync(userId, feed.Id, cancellationToken));
        }

        if (!authorized)
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Challenge(AuthenticationConstants.NugetBasicAuthenticationScheme);
            }

            await AuditAsync(LogLevel.Warning, $"{action}_unauthorized", packageName, packageVersion);
            return Text(StatusCodes.Status403Forbidden, "You do not have permission to change gems of this feed.");
        }

        if (_feedSettings.GetIsReadOnlyMode(feed))
        {
            await AuditAsync(LogLevel.Warning, $"{action}_read_only", packageName, packageVersion);
            return Text(StatusCodes.Status403Forbidden, "This feed is read-only.");
        }

        return null;
    }

    /// <summary>
    /// The API key gem sends: the whole value of the <c>Authorization</c> header.
    /// </summary>
    private string GetApiKey()
    {
        var value = Request.Headers.Authorization.ToString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static ContentResult TooLarge(long maxBytes)
    {
        return Text(StatusCodes.Status413PayloadTooLarge, $"The gem is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.");
    }

    /// <summary>
    /// A plain text answer, which gem push and gem yank show as it is.
    /// </summary>
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
            actor = GetApiKey() == null ? "anonymous" : "api-key";
        }

        await _audit.PackageAsync(HttpContext, level, eventName, _feedContext.CurrentFeed.Slug, packageName, packageVersion, actor);
    }
}
