using System;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Feeds;
using PaGetto.Core.Indexing;
using PaGetto.Web.Audit;
using PaGetto.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace PaGetto.Web.Controllers;

public partial class PackagePublishController : Controller
{
    private const long BytesPerMiB = 1024L * 1024;

    private readonly IAuthenticationService _authentication;
    private readonly IFeedAuthenticationService _feedAuthentication;
    private readonly IPermissionService _permissionService;
    private readonly IFeedContext _feedContext;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IPackageIndexingService _indexer;
    private readonly IPackageDatabase _packages;
    private readonly IPackageDeletionService _deleteService;
    private readonly IOptionsSnapshot<PaGettoOptions> _options;
    private readonly WebAuditLog _audit;
    private readonly ILogger<PackagePublishController> _logger;

    public PackagePublishController(
        IAuthenticationService authentication,
        IFeedAuthenticationService feedAuthentication,
        IPermissionService permissionService,
        IFeedContext feedContext,
        IFeedSettingsResolver feedSettings,
        IPackageIndexingService indexer,
        IPackageDatabase packages,
        IPackageDeletionService deletionService,
        IOptionsSnapshot<PaGettoOptions> options,
        WebAuditLog audit,
        ILogger<PackagePublishController> logger)
    {
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _feedAuthentication = feedAuthentication ?? throw new ArgumentNullException(nameof(feedAuthentication));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _deleteService = deletionService ?? throw new ArgumentNullException(nameof(deletionService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // See: https://docs.microsoft.com/en-us/nuget/api/package-publish-resource#push-a-package
    public async Task Upload(CancellationToken cancellationToken)
    {
        if (_feedSettings.GetIsReadOnlyMode(_feedContext.CurrentFeed))
        {
            var (readOnlyAuthorized, readOnlyDeniedStatus, _) = await AuthorizePushAsync(cancellationToken);
            await AuditAsync(LogLevel.Warning, "package_upload_read_only", null, null, GetActor());
            HttpContext.Response.StatusCode = readOnlyAuthorized ? 403 : readOnlyDeniedStatus;
            return;
        }

        // The package is only read after authorization, so denied uploads are logged without its id and version.
        var (authorized, deniedStatus, actor) = await AuthorizePushAsync(cancellationToken);
        if (!authorized)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_unauthorized", null, null, actor);
            HttpContext.Response.StatusCode = deniedStatus;
            return;
        }

        try
        {
            using var uploadStream = await Request.GetUploadStreamOrNullAsync(cancellationToken);
            if (uploadStream == null)
            {
                await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", null, null, actor);
                HttpContext.Response.StatusCode = 400;
                return;
            }

            var identity = TryReadPackageIdentity(uploadStream);
            var packageId = identity?.Id;
            var packageVersion = identity?.Version?.ToNormalizedString();

            // The server-wide request limit applies before the feed is known; a feed can only lower it.
            var maxBytes = (long)_feedSettings.GetMaxPackageSizeMiB(_feedContext.CurrentFeed) * BytesPerMiB;
            if (uploadStream.Length > maxBytes)
            {
                await AuditAsync(LogLevel.Warning, "package_upload_too_large", packageId, packageVersion, actor);
                HttpContext.Response.StatusCode = 413;
                return;
            }

            var result = await _indexer.IndexAsync(_feedContext.CurrentFeed.Id, _feedContext.CurrentFeed.Slug, uploadStream, cacheFeedUrl: null, published: null, cancellationToken);

            switch (result)
            {
                case PackageIndexingResult.InvalidPackage:
                    await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", packageId, packageVersion, actor);
                    HttpContext.Response.StatusCode = 400;
                    break;

                case PackageIndexingResult.PackageAlreadyExists:
                    await AuditAsync(LogLevel.Warning, "package_upload_already_exists", packageId, packageVersion, actor);
                    HttpContext.Response.StatusCode = 409;
                    break;

                case PackageIndexingResult.Success:
                    await AuditAsync(LogLevel.Information, "package_upload_succeeded", packageId, packageVersion, actor);
                    HttpContext.Response.StatusCode = 201;
                    break;
            }
        }
        catch (Exception e)
        {
            LogUploadException(e);

            HttpContext.Response.StatusCode = 500;
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(string id, string version, CancellationToken cancellationToken)
    {
        if (_feedSettings.GetIsReadOnlyMode(_feedContext.CurrentFeed))
        {
            var (readOnlyAuthorized, readOnlyDeniedStatus, _) = await AuthorizeDeleteAsync(cancellationToken);
            await AuditAsync(LogLevel.Warning, "package_delete_read_only", id, version, GetActor());
            return DeniedResult(readOnlyAuthorized ? 403 : readOnlyDeniedStatus);
        }

        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            await AuditAsync(LogLevel.Warning, "package_delete_not_found", id, version, GetActor());
            return NotFound();
        }

        var (authorized, deniedStatus, actor) = await AuthorizeDeleteAsync(cancellationToken);
        if (!authorized)
        {
            await AuditAsync(LogLevel.Warning, "package_delete_unauthorized", id, version, actor);
            return DeniedResult(deniedStatus);
        }

        if (await _deleteService.TryDeletePackageAsync(_feedContext.CurrentFeed.Id, _feedContext.CurrentFeed.Slug, id, nugetVersion, cancellationToken))
        {
            await AuditAsync(LogLevel.Information, "package_delete_succeeded", id, version, actor);
            return NoContent();
        }
        else
        {
            await AuditAsync(LogLevel.Warning, "package_delete_not_found", id, version, actor);
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<IActionResult> Relist(string id, string version, CancellationToken cancellationToken)
    {
        if (_feedSettings.GetIsReadOnlyMode(_feedContext.CurrentFeed))
        {
            var (readOnlyAuthorized, readOnlyDeniedStatus, _) = await AuthorizePushAsync(cancellationToken);
            await AuditAsync(LogLevel.Warning, "package_relist_read_only", id, version, GetActor());
            return DeniedResult(readOnlyAuthorized ? 403 : readOnlyDeniedStatus);
        }

        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            await AuditAsync(LogLevel.Warning, "package_relist_not_found", id, version, GetActor());
            return NotFound();
        }

        var (authorized, deniedStatus, actor) = await AuthorizePushAsync(cancellationToken);
        if (!authorized)
        {
            await AuditAsync(LogLevel.Warning, "package_relist_unauthorized", id, version, actor);
            return DeniedResult(deniedStatus);
        }

        if (await _packages.RelistPackageAsync(_feedContext.CurrentFeed.Id, id, nugetVersion, cancellationToken))
        {
            await AuditAsync(LogLevel.Information, "package_relist_succeeded", id, version, actor);
            return Ok();
        }
        else
        {
            await AuditAsync(LogLevel.Warning, "package_relist_not_found", id, version, actor);
            return NotFound();
        }
    }

    /// <summary>
    /// Checks the push permission. When it is missing, <c>DeniedStatus</c> is 401 for missing or
    /// wrong credentials and 403 for a known user without the permission, so NuGet clients don't
    /// ask for new credentials when the real problem is a missing permission. A user with no
    /// permission at all on the feed gets 404 instead, the same answer as for a feed that doesn't
    /// exist, so feed slugs can't be probed.
    /// </summary>
    private async Task<(bool Authorized, int DeniedStatus, string Actor)> AuthorizePushAsync(CancellationToken cancellationToken)
    {
        var authMode = _options.Value.Authentication?.Mode ?? AuthenticationMode.Legacy;

        if (authMode == AuthenticationMode.Legacy)
        {
            // Static auth mode: use configured API key
            return (await _authentication.AuthenticateAsync(Request.GetApiKey(), cancellationToken), 401, GetActor());
        }

        return await AuthorizeUserAsync(_permissionService.CanPushAsync, cancellationToken);
    }

    private async Task<(bool Authorized, int DeniedStatus, string Actor)> AuthorizeDeleteAsync(CancellationToken cancellationToken)
    {
        var authMode = _options.Value.Authentication?.Mode ?? AuthenticationMode.Legacy;

        if (authMode == AuthenticationMode.Legacy)
        {
            // Static auth mode has no per-user delete permission; the configured API key
            // governs deletion exactly as it governs push.
            return (await _authentication.AuthenticateAsync(Request.GetApiKey(), cancellationToken), 401, GetActor());
        }

        return await AuthorizeUserAsync(_permissionService.CanDeleteAsync, cancellationToken);
    }

    private async Task<(bool Authorized, int DeniedStatus, string Actor)> AuthorizeUserAsync(
        Func<Guid, Guid, CancellationToken, Task<bool>> hasPermission,
        CancellationToken cancellationToken)
    {
        // Prefer X-NuGet-ApiKey (dotnet nuget push -k <token>), fall back to the user identity
        // already established by Basic auth middleware.
        Guid? userId = null;
        var actor = GetActor();

        var apiKey = Request.GetApiKey();
        if (!string.IsNullOrEmpty(apiKey))
        {
            var authResult = await _feedAuthentication.AuthenticateByTokenAsync(apiKey, cancellationToken);
            if (authResult.IsAuthenticated && authResult.UserId.HasValue)
            {
                userId = authResult.UserId.Value;
                actor = authResult.Username;
            }
        }

        if (userId == null)
        {
            var userIdClaim = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var claimUserId))
                return (false, 401, actor);

            userId = claimUserId;
        }

        var feedId = _feedContext.CurrentFeed.Id;
        if (await hasPermission(userId.Value, feedId, cancellationToken))
            return (true, 403, actor);

        var canSeeFeed = await _permissionService.CanPullAsync(userId.Value, feedId, cancellationToken)
            || await _permissionService.CanPushAsync(userId.Value, feedId, cancellationToken)
            || await _permissionService.CanDeleteAsync(userId.Value, feedId, cancellationToken);

        return (false, canSeeFeed ? 403 : 404, actor);
    }

    private StatusCodeResult DeniedResult(int statusCode)
    {
        return statusCode == 401 ? Unauthorized() : StatusCode(statusCode);
    }

    private string GetActor()
    {
        var name = HttpContext.User.Identity?.Name;
        if (!string.IsNullOrEmpty(name))
            return name;

        // Config mode API keys are shared and carry no user identity.
        return string.IsNullOrEmpty(Request.GetApiKey()) ? "anonymous" : "api-key";
    }

    private async Task AuditAsync(LogLevel level, string eventName, string packageId, string packageVersion, string actor)
    {
        await _audit.PackageAsync(HttpContext, level, eventName, _feedContext.CurrentFeed.Slug, packageId, packageVersion, actor);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Exception thrown during package upload")]
    private partial void LogUploadException(Exception exception);
}
