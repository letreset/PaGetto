using System;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Feeds;
using PaGetto.Core.Indexing;
using PaGetto.Core.Storage;
using PaGetto.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaGetto.Web.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public partial class SymbolController : Controller
{
    private const long BytesPerGiB = 1024L * 1024 * 1024;

    private readonly IAuthenticationService _authentication;
    private readonly IFeedAuthenticationService _feedAuthentication;
    private readonly IPermissionService _permissionService;
    private readonly IFeedContext _feedContext;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly ISymbolIndexingService _indexer;
    private readonly ISymbolStorageService _storage;
    private readonly IOptionsSnapshot<PaGettoOptions> _options;
    private readonly ILogger<SymbolController> _logger;

    public SymbolController(
        IAuthenticationService authentication,
        IFeedAuthenticationService feedAuthentication,
        IPermissionService permissionService,
        IFeedContext feedContext,
        IFeedSettingsResolver feedSettings,
        ISymbolIndexingService indexer,
        ISymbolStorageService storage,
        IOptionsSnapshot<PaGettoOptions> options,
        ILogger<SymbolController> logger)
    {
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _feedAuthentication = feedAuthentication ?? throw new ArgumentNullException(nameof(feedAuthentication));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // See: https://docs.microsoft.com/en-us/nuget/api/package-publish-resource#push-a-package
    public async Task Upload(CancellationToken cancellationToken)
    {
        if (_feedSettings.GetIsReadOnlyMode(_feedContext.CurrentFeed))
        {
            HttpContext.Response.StatusCode = 401;
            return;
        }

        var (authorized, authenticated) = await AuthorizePushAsync(cancellationToken);
        if (!authorized)
        {
            // 403 for a known user without the push permission, 401 for missing or wrong credentials.
            HttpContext.Response.StatusCode = authenticated ? 403 : 401;
            return;
        }

        try
        {
            using var uploadStream = await Request.GetUploadStreamOrNullAsync(cancellationToken);
            if (uploadStream == null)
            {
                HttpContext.Response.StatusCode = 400;
                return;
            }

            // The server-wide request limit applies before the feed is known; a feed can only lower it.
            var maxBytes = (long)_feedSettings.GetMaxPackageSizeGiB(_feedContext.CurrentFeed) * BytesPerGiB;
            if (uploadStream.Length > maxBytes)
            {
                LogSymbolUploadTooLarge("symbol_upload_too_large", _feedContext.CurrentFeed.Slug, HttpContext.User.Identity?.Name ?? "anonymous", HttpContext.Connection.RemoteIpAddress);
                HttpContext.Response.StatusCode = 413;
                return;
            }

            var result = await _indexer.IndexAsync(_feedContext.CurrentFeed.Id, _feedContext.CurrentFeed.Slug, uploadStream, cancellationToken);

            switch (result)
            {
                case SymbolIndexingResult.InvalidSymbolPackage:
                    HttpContext.Response.StatusCode = 400;
                    break;

                case SymbolIndexingResult.PackageNotFound:
                    HttpContext.Response.StatusCode = 404;
                    break;

                case SymbolIndexingResult.Success:
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

    public async Task<IActionResult> Get(string file, string key)
    {
        var pdbStream = await _storage.GetPortablePdbContentStreamOrNullAsync(_feedContext.CurrentFeed.Slug, file, key);
        if (pdbStream == null)
        {
            return NotFound();
        }

        return File(pdbStream, "application/octet-stream");
    }

    private async Task<(bool Authorized, bool Authenticated)> AuthorizePushAsync(CancellationToken cancellationToken)
    {
        var authMode = _options.Value.Authentication?.Mode ?? AuthenticationMode.Legacy;

        if (authMode == AuthenticationMode.Legacy)
        {
            // Static auth mode: use configured API key
            return (await _authentication.AuthenticateAsync(Request.GetApiKey(), cancellationToken), false);
        }

        var feedId = _feedContext.CurrentFeed.Id;

        // New mode: prefer X-NuGet-ApiKey (dotnet nuget push -k <token>), fall back to
        // the user identity already established by Basic auth middleware.
        var apiKey = Request.GetApiKey();
        if (!string.IsNullOrEmpty(apiKey))
        {
            var authResult = await _feedAuthentication.AuthenticateByTokenAsync(apiKey, cancellationToken);
            if (authResult.IsAuthenticated && authResult.UserId.HasValue)
                return (await _permissionService.CanPushAsync(authResult.UserId.Value, feedId, cancellationToken), true);
        }

        var userIdClaim = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var userId))
            return (await _permissionService.CanPushAsync(userId, feedId, cancellationToken), true);

        return (false, false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Exception thrown during symbol upload")]
    private partial void LogUploadException(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AUDIT {Event} feed={Feed} actor={Actor} ip={Ip}")]
    private partial void LogSymbolUploadTooLarge(string @event, string feed, string actor, IPAddress ip);
}
