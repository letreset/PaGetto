using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Feeds;
using PaGetto.Core.Indexing;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace PaGetto.Web.Pages;

/// <summary>
/// Shows push instructions and uploads packages from the browser. The upload handlers apply the
/// same rules as <c>PUT api/v2/package</c> and <c>PUT api/v2/symbol</c> (read-only feed, push
/// permission, feed size limit, duplicate versions) and write the same <c>AUDIT</c> events.
/// </summary>
public partial class UploadModel : PageModel
{
    private const long BytesPerGiB = 1024L * 1024 * 1024;

    private readonly IPermissionService _permissions;
    private readonly IFeedContext _feedContext;
    private readonly IOptionsSnapshot<NugetAuthenticationOptions> _authOptions;
    private readonly IAuthenticationService _authentication;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IPackageIndexingService _packageIndexer;
    private readonly ISymbolIndexingService _symbolIndexer;
    private readonly IPackageDatabase _packages;
    private readonly WebAuditLog _audit;
    private readonly ILogger<UploadModel> _logger;

    public UploadModel(
        IPermissionService permissions,
        IFeedContext feedContext,
        IOptionsSnapshot<NugetAuthenticationOptions> authOptions,
        IAuthenticationService authentication,
        IFeedSettingsResolver feedSettings,
        IPackageIndexingService packageIndexer,
        ISymbolIndexingService symbolIndexer,
        IPackageDatabase packages,
        WebAuditLog audit,
        ILogger<UploadModel> logger)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _authOptions = authOptions ?? throw new ArgumentNullException(nameof(authOptions));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _packageIndexer = packageIndexer ?? throw new ArgumentNullException(nameof(packageIndexer));
        _symbolIndexer = symbolIndexer ?? throw new ArgumentNullException(nameof(symbolIndexer));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// True when the feed accepts no uploads; the page then hides the browser upload.
    /// </summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>
    /// True in Config mode when an API key is configured, so the upload form asks for it.
    /// </summary>
    public bool ApiKeyRequired { get; private set; }

    public uint MaxPackageSizeGiB { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var authMode = _authOptions.Value.Mode;
        if (FeedAccessGuard.RequiresSignIn(HttpContext, authMode)) return Page();

        var denied = await CheckPageAccessAsync(authMode, cancellationToken);
        if (denied != null) return denied;

        IsReadOnly = _feedSettings.GetIsReadOnlyMode(_feedContext.CurrentFeed);
        MaxPackageSizeGiB = _feedSettings.GetMaxPackageSizeGiB(_feedContext.CurrentFeed);

        // The key check accepts anything when no key is configured.
        ApiKeyRequired = authMode == AuthenticationMode.Config
            && !await _authentication.AuthenticateAsync(null, cancellationToken);

        return Page();
    }

    /// <summary>
    /// Tells the preview whether a version already exists in the current feed, and whether
    /// pushing it again would replace it.
    /// </summary>
    public async Task<IActionResult> OnGetCheckAsync(string id, string version, CancellationToken cancellationToken)
    {
        var authMode = _authOptions.Value.Mode;
        if (FeedAccessGuard.RequiresSignIn(HttpContext, authMode)) return Unauthorized();

        var denied = await CheckPageAccessAsync(authMode, cancellationToken);
        if (denied != null) return denied;

        if (string.IsNullOrWhiteSpace(id) || !NuGetVersion.TryParse(version, out var nugetVersion))
            return BadRequest();

        var feed = _feedContext.CurrentFeed;
        var exists = await _packages.ExistsAsync(feed.Id, id, nugetVersion, cancellationToken);
        var overwrites = _feedSettings.GetAllowPackageOverwrites(feed);
        var canOverwrite = overwrites == PackageOverwriteAllowed.True
            || (overwrites == PackageOverwriteAllowed.PrereleaseOnly && nugetVersion.IsPrerelease);

        return new JsonResult(new { exists, canOverwrite });
    }

    public async Task<IActionResult> OnPostPackageAsync(CancellationToken cancellationToken)
    {
        var denied = await AuthorizeUploadAsync("package", cancellationToken);
        if (denied != null) return denied;

        try
        {
            using var uploadStream = await Request.GetUploadStreamOrNullAsync(cancellationToken);
            if (uploadStream == null)
                return Audited(StatusCodes.Status400BadRequest, "package_upload_invalid_package", "invalid", "The file is not a valid package.");

            var identity = TryReadPackageIdentity(uploadStream);
            var packageId = identity?.Id;
            var packageVersion = identity?.Version?.ToNormalizedString();

            if (uploadStream.Length > MaxUploadBytes())
                return Audited(StatusCodes.Status413PayloadTooLarge, "package_upload_too_large", "too_large", TooLargeMessage(), packageId, packageVersion);

            var result = await _packageIndexer.IndexAsync(
                _feedContext.CurrentFeed.Id, _feedContext.CurrentFeed.Slug, uploadStream, cacheFeedUrl: null, published: null, cancellationToken);

            return result switch
            {
                PackageIndexingResult.Success => Audited(
                    StatusCodes.Status201Created, "package_upload_succeeded", "published", "Published.", packageId, packageVersion),
                PackageIndexingResult.PackageAlreadyExists => Audited(
                    StatusCodes.Status409Conflict, "package_upload_already_exists", "exists", "This version already exists in the feed.", packageId, packageVersion),
                _ => Audited(
                    StatusCodes.Status400BadRequest, "package_upload_invalid_package", "invalid", "The file is not a valid package.", packageId, packageVersion),
            };
        }
        catch (Exception e)
        {
            LogUploadException(e);
            return Outcome(StatusCodes.Status500InternalServerError, "error", "The upload failed on the server.");
        }
    }

    public async Task<IActionResult> OnPostSymbolAsync(CancellationToken cancellationToken)
    {
        var denied = await AuthorizeUploadAsync("symbol", cancellationToken);
        if (denied != null) return denied;

        try
        {
            using var uploadStream = await Request.GetUploadStreamOrNullAsync(cancellationToken);
            if (uploadStream == null)
                return Audited(StatusCodes.Status400BadRequest, "symbol_upload_invalid_package", "invalid", "The file is not a valid symbol package.");

            var identity = TryReadPackageIdentity(uploadStream);
            var packageId = identity?.Id;
            var packageVersion = identity?.Version?.ToNormalizedString();

            if (uploadStream.Length > MaxUploadBytes())
                return Audited(StatusCodes.Status413PayloadTooLarge, "symbol_upload_too_large", "too_large", TooLargeMessage(), packageId, packageVersion);

            var result = await _symbolIndexer.IndexAsync(
                _feedContext.CurrentFeed.Id, _feedContext.CurrentFeed.Slug, uploadStream, cancellationToken);

            return result switch
            {
                SymbolIndexingResult.Success => Audited(
                    StatusCodes.Status201Created, "symbol_upload_succeeded", "published", "Published.", packageId, packageVersion),
                SymbolIndexingResult.PackageNotFound => Audited(
                    StatusCodes.Status404NotFound, "symbol_upload_package_not_found", "not_found", "Upload the package before its symbols.", packageId, packageVersion),
                _ => Audited(
                    StatusCodes.Status400BadRequest, "symbol_upload_invalid_package", "invalid", "The file is not a valid symbol package.", packageId, packageVersion),
            };
        }
        catch (Exception e)
        {
            LogUploadException(e);
            return Outcome(StatusCodes.Status500InternalServerError, "error", "The upload failed on the server.");
        }
    }

    /// <summary>
    /// The page is only for users who can push, and the Upload link is hidden for everyone else,
    /// so pull-only users get the same 404 as a missing page.
    /// </summary>
    private async Task<IActionResult> CheckPageAccessAsync(AuthenticationMode authMode, CancellationToken cancellationToken)
    {
        var denied = await FeedAccessGuard.CheckReadAccessAsync(
            HttpContext, _feedContext, _permissions, authMode, cancellationToken);
        if (denied != null) return denied;

        if (!await FeedAccessGuard.CanPushToCurrentFeedAsync(
                HttpContext, _feedContext, _permissions, authMode, cancellationToken))
        {
            return NotFound();
        }

        return null;
    }

    /// <summary>
    /// Returns null when the upload may proceed. The permission is checked before the feed's
    /// read-only flag, so visitors who can't push learn nothing about the feed. As in the API,
    /// denied uploads are logged without the package id and version.
    /// </summary>
    private async Task<IActionResult> AuthorizeUploadAsync(string kind, CancellationToken cancellationToken)
    {
        var authMode = _authOptions.Value.Mode;

        if (authMode == AuthenticationMode.Config)
        {
            if (!await _authentication.AuthenticateAsync(Request.GetApiKey(), cancellationToken))
                return Audited(StatusCodes.Status401Unauthorized, $"{kind}_upload_unauthorized", "unauthorized", "The API key is missing or wrong.");
        }
        else if (FeedAccessGuard.RequiresSignIn(HttpContext, authMode))
        {
            return Audited(StatusCodes.Status401Unauthorized, $"{kind}_upload_unauthorized", "unauthorized", "Sign in to upload packages.");
        }
        else if (!await FeedAccessGuard.CanPushToCurrentFeedAsync(HttpContext, _feedContext, _permissions, authMode, cancellationToken))
        {
            return Audited(StatusCodes.Status403Forbidden, $"{kind}_upload_unauthorized", "unauthorized", "You can't push to this feed.");
        }

        if (_feedSettings.GetIsReadOnlyMode(_feedContext.CurrentFeed))
            return Audited(StatusCodes.Status403Forbidden, $"{kind}_upload_read_only", "read_only", "This feed is read-only.");

        return null;
    }

    private long MaxUploadBytes()
    {
        return (long)_feedSettings.GetMaxPackageSizeGiB(_feedContext.CurrentFeed) * BytesPerGiB;
    }

    private string TooLargeMessage()
    {
        return $"The file is larger than this feed's {_feedSettings.GetMaxPackageSizeGiB(_feedContext.CurrentFeed)} GiB limit.";
    }

    private JsonResult Audited(int statusCode, string eventName, string outcome, string message, string packageId = null, string packageVersion = null)
    {
        var level = statusCode < 400 ? LogLevel.Information : LogLevel.Warning;

        // Config mode API keys are shared and carry no user identity.
        var actor = _authOptions.Value.Mode == AuthenticationMode.Config && !string.IsNullOrEmpty(Request.GetApiKey())
            ? "api-key"
            : null;
        _audit.Package(HttpContext, level, eventName, _feedContext.CurrentFeed.Slug, packageId, packageVersion, actor);

        return Outcome(statusCode, outcome, message, packageId, packageVersion);
    }

    private JsonResult Outcome(int statusCode, string outcome, string message, string packageId = null, string packageVersion = null)
    {
        var url = statusCode == StatusCodes.Status201Created
            ? Url.Page("/Package", new { id = packageId, version = packageVersion })
            : null;

        return new JsonResult(new { outcome, message, id = packageId, version = packageVersion, url })
        {
            StatusCode = statusCode,
        };
    }

    private static PackageIdentity TryReadPackageIdentity(Stream packageStream)
    {
        try
        {
            using var reader = new PackageArchiveReader(packageStream, leaveStreamOpen: true);
            return reader.GetIdentity();
        }
        catch (Exception)
        {
            // The indexer rejects the package and logs why.
            return null;
        }
        finally
        {
            packageStream.Position = 0;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Exception thrown during package upload from the web UI")]
    private partial void LogUploadException(Exception exception);
}
