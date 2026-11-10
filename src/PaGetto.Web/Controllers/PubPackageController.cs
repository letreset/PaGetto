using System;
using System.Buffers;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Pub;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The pub API of Dart feeds, used by <c>dart pub</c> and <c>flutter pub</c>.
/// See: https://github.com/dart-lang/pub/blob/master/doc/repository-spec-v2.md
/// </summary>
[FeedType(FeedType.Pub)]
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public partial class PubPackageController : Controller
{
    private const string PubMediaType = "application/vnd.pub.v2+json";
    private const long BytesPerMiB = 1024L * 1024;

    private readonly IPubPackageService _packages;
    private readonly IFeedContext _feedContext;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IAuthenticationService _authentication;
    private readonly IPermissionService _permissionService;
    private readonly IOptionsSnapshot<PaGettoOptions> _options;
    private readonly WebAuditLog _audit;
    private readonly ILogger<PubPackageController> _logger;

    public PubPackageController(
        IPubPackageService packages,
        IFeedContext feedContext,
        IFeedSettingsResolver feedSettings,
        IAuthenticationService authentication,
        IPermissionService permissionService,
        IOptionsSnapshot<PaGettoOptions> options,
        WebAuditLog audit,
        ILogger<PubPackageController> logger)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // GET api/packages/{name}
    public async Task<IActionResult> GetPackage(string name, CancellationToken cancellationToken)
    {
        var package = await _packages.FindPackageOrNullAsync(_feedContext.CurrentFeed, name, cancellationToken);
        if (package == null)
        {
            return PackageNotFound(name);
        }

        return PubJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("name", package.Name);

            writer.WritePropertyName("latest");
            WriteVersion(writer, package.Name, package.Latest);

            writer.WriteStartArray("versions");
            foreach (var version in package.Versions)
            {
                WriteVersion(writer, package.Name, version);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    // GET api/packages/{name}/versions/{version}
    // Deprecated in the specification, but still used by older clients.
    public async Task<IActionResult> GetPackageVersion(string name, string version, CancellationToken cancellationToken)
    {
        var package = await _packages.FindPackageOrNullAsync(_feedContext.CurrentFeed, name, cancellationToken);
        if (package == null)
        {
            return PackageNotFound(name);
        }

        foreach (var candidate in package.Versions)
        {
            if (string.Equals(candidate.Version, version, StringComparison.OrdinalIgnoreCase))
            {
                return PubJson(writer => WriteVersion(writer, package.Name, candidate));
            }
        }

        return PubError(404, "NotFound", $"Version {version} of package {name} was not found.");
    }

    // GET packages/{name}/versions/{version}.tar.gz
    public async Task<IActionResult> DownloadArchive(string name, string version, CancellationToken cancellationToken)
    {
        var archive = await _packages.GetArchiveStreamOrNullAsync(_feedContext.CurrentFeed, name, version, cancellationToken);
        if (archive == null)
        {
            return PubError(404, "NotFound", $"Version {version} of package {name} was not found.");
        }

        return File(archive, "application/octet-stream", $"{name}-{version}.tar.gz");
    }

    // GET api/packages/versions/new
    // The first step of "dart pub publish": tells the client where to upload the archive.
    public async Task<IActionResult> NewVersion(CancellationToken cancellationToken)
    {
        var denied = await CheckCanPublishAsync(cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        return PubJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("url", Url.RouteUrl(Routes.PubUploadRouteName, values: null, Request.Scheme));
            writer.WriteStartObject("fields");
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
    }

    // POST api/packages/versions/newUpload
    // The second step: the archive, as the "file" part of a multipart form. The package is published
    // here. The client only shows server messages from the last step, so the outcome is passed on
    // to it in the redirect.
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        var denied = await CheckCanPublishAsync(cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        var feed = _feedContext.CurrentFeed;

        try
        {
            using var archive = await Request.GetUploadStreamOrNullAsync(cancellationToken);
            if (archive == null || archive.Length == 0)
            {
                await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", null, null);
                return FinishUploadRedirect(error: "The upload did not contain a package archive.");
            }

            // The server-wide request limit applies before the feed is known; a feed can only lower it.
            var maxMiB = _feedSettings.GetMaxPackageSizeMiB(feed);
            if (archive.Length > maxMiB * BytesPerMiB)
            {
                await AuditAsync(LogLevel.Warning, "package_upload_too_large", null, null);
                return FinishUploadRedirect(error: $"The package is larger than the feed's limit of {maxMiB} MiB.");
            }

            var result = await _packages.PublishAsync(feed, archive, cancellationToken);

            switch (result.Status)
            {
                case PubPublishStatus.Success:
                    await AuditAsync(LogLevel.Information, "package_upload_succeeded", result.Name, result.Version);
                    return FinishUploadRedirect(result.Name, result.Version);

                case PubPublishStatus.AlreadyExists:
                    await AuditAsync(LogLevel.Warning, "package_upload_already_exists", result.Name, result.Version);
                    return FinishUploadRedirect(error: result.Message);

                default:
                    await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", result.Name, result.Version);
                    return FinishUploadRedirect(error: result.Message);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogUploadException(e);

            return StatusCode(500);
        }
    }

    // GET api/packages/versions/newUploadFinish
    // The last step: reports the outcome of the upload.
    public async Task<IActionResult> FinishUpload(string package, string version, string error, CancellationToken cancellationToken)
    {
        var denied = await CheckCanPublishAsync(cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        if (!string.IsNullOrEmpty(error))
        {
            return PubError(400, "PackageRejected", error);
        }

        // Only confirm what is really there, rather than whatever the query string says.
        var published = await _packages.FindPackageOrNullAsync(_feedContext.CurrentFeed, package, cancellationToken);
        var exists = false;
        foreach (var candidate in published?.Versions ?? [])
        {
            exists |= string.Equals(candidate.Version, version, StringComparison.OrdinalIgnoreCase);
        }

        if (!exists)
        {
            return PubError(400, "PackageRejected", "The package was not published.");
        }

        return PubJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartObject("success");
            writer.WriteString("message", $"Successfully uploaded {package} version {version}.");
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
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
            // Static auth mode: the configured API key, sent as the pub token.
            authorized = await _authentication.AuthenticateAsync(GetBearerToken(), cancellationToken);
        }
        else
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            authorized = Guid.TryParse(userIdClaim, out var userId)
                && await _permissionService.CanPushAsync(userId, feed.Id, cancellationToken);
        }

        if (!authorized)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_unauthorized", null, null);

            // 403 rather than 401: the client keeps its token, which is valid for reading.
            Response.Headers.WWWAuthenticate = NugetBasicAuthenticationHandler.PubBearerChallenge(
                "You do not have permission to publish packages to this feed.");
            return StatusCode(403);
        }

        if (_feedSettings.GetIsReadOnlyMode(feed))
        {
            await AuditAsync(LogLevel.Warning, "package_upload_read_only", null, null);
            return PubError(400, "ReadOnly", "This feed is read-only.");
        }

        return null;
    }

    private string GetBearerToken()
    {
        return AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            ? header.Parameter
            : null;
    }

    private NoContentResult FinishUploadRedirect(string package = null, string version = null, string error = null)
    {
        Response.Headers.Location = Url.RouteUrl(Routes.PubFinishUploadRouteName, new { package, version, error }, Request.Scheme);

        return NoContent();
    }

    private void WriteVersion(Utf8JsonWriter writer, string name, PubPackageVersion version)
    {
        writer.WriteStartObject();
        writer.WriteString("version", version.Version);

        if (version.Retracted)
        {
            writer.WriteBoolean("retracted", true);
        }

        writer.WriteString("archive_url", Url.RouteUrl(Routes.PubArchiveRouteName, new { name, version = version.Version }, Request.Scheme));

        if (!string.IsNullOrEmpty(version.ArchiveSha256))
        {
            writer.WriteString("archive_sha256", version.ArchiveSha256);
        }

        writer.WritePropertyName("pubspec");
        writer.WriteRawValue(version.PubspecJson);

        if (version.Published.HasValue)
        {
            writer.WriteString("published", DateTime.SpecifyKind(version.Published.Value, DateTimeKind.Utc));
        }

        writer.WriteEndObject();
    }

    private static ContentResult PubJson(Action<Utf8JsonWriter> write, int statusCode = 200)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        return new ContentResult
        {
            StatusCode = statusCode,
            ContentType = PubMediaType,
            Content = Encoding.UTF8.GetString(buffer.WrittenSpan),
        };
    }

    private static ContentResult PackageNotFound(string name)
    {
        return PubError(404, "NotFound", $"Package {name} was not found.");
    }

    private static ContentResult PubError(int statusCode, string code, string message)
    {
        return PubJson(
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteStartObject("error");
                writer.WriteString("code", code);
                writer.WriteString("message", message);
                writer.WriteEndObject();
                writer.WriteEndObject();
            },
            statusCode);
    }

    private async Task AuditAsync(LogLevel level, string eventName, string packageName, string packageVersion)
    {
        var actor = User.Identity?.Name;
        if (string.IsNullOrEmpty(actor))
        {
            // Config mode API keys are shared and carry no user identity.
            actor = string.IsNullOrEmpty(GetBearerToken()) ? "anonymous" : "api-key";
        }

        await _audit.PackageAsync(HttpContext, level, eventName, _feedContext.CurrentFeed.Slug, packageName, packageVersion, actor);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Exception thrown during pub package upload")]
    private partial void LogUploadException(Exception exception);
}
