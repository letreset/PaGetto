using System;
using System.Linq;
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
using PaGetto.Core.Maven;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The Maven repository of Maven feeds, used by Maven, Gradle and sbt. It is at <c>maven2/</c>
/// under the feed, in the Maven repository layout: there is no API besides the paths.
/// See: https://maven.apache.org/repositories/layout.html
/// </summary>
[FeedType(FeedType.Maven)]
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class MavenRepositoryController : Controller
{
    private const long BytesPerMiB = 1024L * 1024;

    private readonly IMavenRepositoryService _repository;
    private readonly IFeedContext _feedContext;
    private readonly FeedChangeAuthorizer _changes;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly WebAuditLog _audit;

    public MavenRepositoryController(
        IMavenRepositoryService repository,
        IFeedContext feedContext,
        FeedChangeAuthorizer changes,
        IFeedSettingsResolver feedSettings,
        WebAuditLog audit)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    // GET, HEAD maven2/{**path}
    public async Task<IActionResult> Get(string path, CancellationToken cancellationToken)
    {
        if (!MavenPath.TryParse(path, out var mavenPath))
        {
            return MavenError(StatusCodes.Status404NotFound, "Not found.");
        }

        var content = await _repository.GetAsync(
            _feedContext.CurrentFeed,
            mavenPath,
            countDownload: !HttpMethods.IsHead(Request.Method),
            cancellationToken);

        if (content == null)
        {
            return MavenError(StatusCodes.Status404NotFound, $"{path} was not found.");
        }

        if (content.File is not { } file)
        {
            return File(content.Stream, content.ContentType);
        }

        // Maven reads the checksums from these headers instead of downloading the checksum files.
        Response.Headers["X-Checksum-SHA1"] = file.Sha1;
        Response.Headers["X-Checksum-MD5"] = file.Md5;
        Response.Headers["X-Checksum-SHA256"] = file.Sha256;

        return File(
            content.Stream,
            content.ContentType,
            new DateTimeOffset(DateTime.SpecifyKind(file.CreatedAtUtc, DateTimeKind.Utc)),
            new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{file.Sha1}\""));
    }

    // PUT maven2/{**path}
    // mvn deploy and Gradle upload each file, then its checksums, then the metadata.
    public async Task<IActionResult> Put(string path, CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;

        if (!MavenPath.TryParse(path, out var mavenPath))
        {
            await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", null, null);
            return MavenError(StatusCodes.Status400BadRequest, $"{path} is not a path of the Maven repository layout that this server stores.");
        }

        var packageName = mavenPath.PackageName;
        var version = mavenPath.Version;

        var denied = await CheckCanPublishAsync(packageName, version, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        // The server-wide request limit applies before the feed is known; a feed can only lower it.
        var maxBytes = _feedSettings.GetMaxPackageSizeBytes(feed);
        if (Request.ContentLength > maxBytes)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", packageName, version);
            return TooLarge(maxBytes);
        }

        var sizeLimit = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = maxBytes;
        }

        MavenPutResult result;
        try
        {
            result = await _repository.PutAsync(feed, mavenPath, Request.Body, maxBytes, cancellationToken);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", packageName, version);
            return TooLarge(maxBytes);
        }

        switch (result.Status)
        {
            case MavenPutStatus.Success:
                // Checksums and metadata are written by the server, so only stored files are audited.
                if (result.StoredFile)
                {
                    await AuditAsync(LogLevel.Information, "package_upload_succeeded", packageName, version);
                }

                return StatusCode(StatusCodes.Status201Created);

            case MavenPutStatus.Conflict:
                await AuditAsync(LogLevel.Warning, "package_upload_already_exists", packageName, version);
                return MavenError(StatusCodes.Status409Conflict, result.Message);

            case MavenPutStatus.TooLarge:
                await AuditAsync(LogLevel.Warning, "package_upload_too_large", packageName, version);
                return MavenError(StatusCodes.Status413PayloadTooLarge, result.Message);

            default:
                await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", packageName, version);
                return MavenError(StatusCodes.Status400BadRequest, result.Message);
        }
    }

    /// <summary>
    /// Returns null when the caller may publish to the current feed, or the response to send
    /// otherwise. The caller is already known to be allowed to read the feed.
    /// </summary>
    private async Task<IActionResult> CheckCanPublishAsync(string packageName, string version, CancellationToken cancellationToken)
    {
        var access = await _changes.CheckAsync(User, Request.GetApiKeyOrPassword(), FeedPermissionRequirement.Push, cancellationToken);
        if (access == FeedChangeAccess.Unauthorized)
        {
            // Maven and Gradle only send their credentials when they are challenged for them.
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Challenge(AuthenticationConstants.NugetBasicAuthenticationScheme);
            }

            await AuditAsync(LogLevel.Warning, "package_upload_unauthorized", packageName, version);
            return MavenError(StatusCodes.Status403Forbidden, "You do not have permission to publish packages to this feed.");
        }

        if (access == FeedChangeAccess.ReadOnly)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_read_only", packageName, version);
            return MavenError(StatusCodes.Status403Forbidden, "This feed is read-only.");
        }

        return null;
    }

    private ContentResult TooLarge(long maxBytes)
    {
        return MavenError(StatusCodes.Status413PayloadTooLarge, $"The file is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.");
    }

    /// <summary>
    /// An error as plain text, with the message as the reason phrase too: Maven and Gradle show
    /// the reason phrase of a failed upload, not its body.
    /// </summary>
    private ContentResult MavenError(int statusCode, string message)
    {
        var response = HttpContext.Features.Get<IHttpResponseFeature>();
        if (response != null)
        {
            // A reason phrase is printable ASCII.
            response.ReasonPhrase = new string(message.Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray());
        }

        return new ContentResult
        {
            StatusCode = statusCode,
            ContentType = "text/plain; charset=utf-8",
            Content = message,
        };
    }

    private async Task AuditAsync(LogLevel level, string eventName, string packageName, string packageVersion)
    {
        var actor = User.Identity?.Name;
        if (string.IsNullOrEmpty(actor))
        {
            // Config mode API keys are shared and carry no user identity.
            actor = string.IsNullOrEmpty(Request.GetApiKeyOrPassword()) ? "anonymous" : "api-key";
        }

        await _audit.PackageAsync(HttpContext, level, eventName, _feedContext.CurrentFeed.Slug, packageName, packageVersion, actor);
    }
}
