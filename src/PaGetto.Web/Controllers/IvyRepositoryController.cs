using System;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
using PaGetto.Core.Ivy;
using PaGetto.Core.Maven;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The Ivy repository of Ivy feeds, used by sbt with <c>Resolver.ivyStylePatterns</c>. It is at
/// <c>ivy/</c> under the feed, in the Ivy layout of sbt: there is no API besides the paths, and the
/// folder of a module is a directory listing of its revisions.
/// See: https://www.scala-sbt.org/1.x/docs/Resolvers.html#Custom+Layout
/// </summary>
[FeedType(FeedType.Ivy)]
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class IvyRepositoryController : Controller
{
    private const long BytesPerMiB = 1024L * 1024;

    private readonly IIvyRepositoryService _repository;
    private readonly IFeedContext _feedContext;
    private readonly FeedChangeAuthorizer _changes;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly WebAuditLog _audit;

    public IvyRepositoryController(
        IIvyRepositoryService repository,
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

    // GET, HEAD ivy/{**path}
    public async Task<IActionResult> Get(string path, CancellationToken cancellationToken)
    {
        if (!IvyPath.TryParse(path, out var ivyPath))
        {
            return IvyError(StatusCodes.Status404NotFound, "Not found.");
        }

        if (ivyPath.IsDirectory)
        {
            return await ListRevisionsAsync(ivyPath, cancellationToken);
        }

        var content = await _repository.GetAsync(
            _feedContext.CurrentFeed,
            ivyPath,
            countDownload: !HttpMethods.IsHead(Request.Method),
            cancellationToken);

        if (content == null)
        {
            return IvyError(StatusCodes.Status404NotFound, $"{path} was not found.");
        }

        if (content.File is not { } file)
        {
            return File(content.Stream, content.ContentType);
        }

        Response.Headers["X-Checksum-SHA1"] = file.Sha1;
        Response.Headers["X-Checksum-MD5"] = file.Md5;
        Response.Headers["X-Checksum-SHA256"] = file.Sha256;

        return File(
            content.Stream,
            content.ContentType,
            new DateTimeOffset(DateTime.SpecifyKind(file.CreatedAtUtc, DateTimeKind.Utc)),
            new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{file.Sha1}\""));
    }

    // PUT ivy/{**path}
    // sbt uploads each artifact with its checksums, and the ivy.xml last.
    public async Task<IActionResult> Put(string path, CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;

        if (!IvyPath.TryParse(path, out var ivyPath) || ivyPath.IsDirectory)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", null, null);
            return IvyError(StatusCodes.Status400BadRequest, $"{path} is not a path of the Ivy layout of sbt (Resolver.ivyStylePatterns) that this server stores.");
        }

        var packageName = ivyPath.PackageName;
        var version = ivyPath.Revision;

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
            result = await _repository.PutAsync(feed, ivyPath, Request.Body, maxBytes, cancellationToken);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", packageName, version);
            return TooLarge(maxBytes);
        }

        switch (result.Status)
        {
            case MavenPutStatus.Success:
                // Checksums are computed by the server, so only stored files are audited.
                if (result.StoredFile)
                {
                    await AuditAsync(LogLevel.Information, "package_upload_succeeded", packageName, version);
                }

                return StatusCode(StatusCodes.Status201Created);

            case MavenPutStatus.Conflict:
                await AuditAsync(LogLevel.Warning, "package_upload_already_exists", packageName, version);
                return IvyError(StatusCodes.Status409Conflict, result.Message);

            case MavenPutStatus.TooLarge:
                await AuditAsync(LogLevel.Warning, "package_upload_too_large", packageName, version);
                return IvyError(StatusCodes.Status413PayloadTooLarge, result.Message);

            default:
                await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", packageName, version);
                return IvyError(StatusCodes.Status400BadRequest, result.Message);
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
            // sbt only sends its credentials when it is challenged for them.
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Challenge(AuthenticationConstants.NugetBasicAuthenticationScheme);
            }

            await AuditAsync(LogLevel.Warning, "package_upload_unauthorized", packageName, version);
            return IvyError(StatusCodes.Status403Forbidden, "You do not have permission to publish packages to this feed.");
        }

        if (access == FeedChangeAccess.ReadOnly)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_read_only", packageName, version);
            return IvyError(StatusCodes.Status403Forbidden, "This feed is read-only.");
        }

        return null;
    }

    private ContentResult TooLarge(long maxBytes)
    {
        return IvyError(StatusCodes.Status413PayloadTooLarge, $"The file is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.");
    }

    /// <summary>
    /// The directory listing of a module's folder: a link to the folder of each revision, which is
    /// how Ivy and Coursier find the revisions of a dynamic revision such as <c>1.+</c>.
    /// </summary>
    private async Task<IActionResult> ListRevisionsAsync(IvyPath path, CancellationToken cancellationToken)
    {
        var revisions = await _repository.ListRevisionsAsync(_feedContext.CurrentFeed, path, cancellationToken);
        if (revisions.Count == 0)
        {
            return IvyError(StatusCodes.Status404NotFound, $"{path.FilePath}/ was not found.");
        }

        var title = WebUtility.HtmlEncode($"Index of {path.FilePath}/");
        var html = new StringBuilder($"<!DOCTYPE html>\n<html>\n<head><title>{title}</title></head>\n<body>\n<h1>{title}</h1>\n<pre>\n");
        foreach (var revision in revisions)
        {
            var link = WebUtility.HtmlEncode(Uri.EscapeDataString(revision) + "/");
            html.Append($"<a href=\"{link}\">{WebUtility.HtmlEncode(revision)}/</a>\n");
        }

        html.Append("</pre>\n</body>\n</html>\n");

        return Content(html.ToString(), "text/html; charset=utf-8");
    }

    /// <summary>
    /// An error as plain text, with the message as the reason phrase too: sbt shows the reason
    /// phrase of a failed upload, not its body.
    /// </summary>
    private ContentResult IvyError(int statusCode, string message)
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
        await _audit.PackageApiAsync(HttpContext, level, eventName, _feedContext.CurrentFeed.Slug, packageName, packageVersion, Request.GetApiKeyOrPassword());
    }
}
