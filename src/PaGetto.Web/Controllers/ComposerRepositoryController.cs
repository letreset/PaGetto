using System;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Authentication;
using PaGetto.Core.Composer;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The Composer repository of Composer feeds, at <c>composer/</c> under the feed: the Composer v2
/// repository format, which Composer reads, and an upload of package zips, which Composer has no
/// protocol for. The upload's path is the one Nexus Repository uses.
/// See: https://getcomposer.org/doc/05-repositories.md#composer
/// </summary>
[FeedType(FeedType.Composer)]
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class ComposerRepositoryController : Controller
{
    private const long BytesPerMiB = 1024L * 1024;

    private const string JsonContentType = "application/json";
    private const string TextContentType = "text/plain; charset=utf-8";
    private const string DevSuffix = "~dev";

    private readonly IComposerPackageService _packages;
    private readonly IFeedContext _feedContext;
    private readonly FeedChangeAuthorizer _changes;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly WebAuditLog _audit;

    public ComposerRepositoryController(
        IComposerPackageService packages,
        IFeedContext feedContext,
        FeedChangeAuthorizer changes,
        IFeedSettingsResolver feedSettings,
        WebAuditLog audit)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    // GET composer/packages.json
    public IActionResult Root()
    {
        // Composer 2 reads a package's versions from the metadata URL. It treats a path that starts
        // with a slash as relative to the host, so the URL is absolute.
        var root = new JsonObject
        {
            ["packages"] = new JsonArray(),
            ["metadata-url"] = Url.ComposerRepository() + "/p2/%package%.json",
        };

        return Content(root.ToJsonString(), JsonContentType);
    }

    // GET composer/p2/{vendor}/{package}.json and composer/p2/{vendor}/{package}~dev.json
    public async Task<IActionResult> Metadata(string vendor, string file, CancellationToken cancellationToken)
    {
        if (!file.EndsWith(".json", StringComparison.Ordinal))
        {
            return NotFound();
        }

        var package = file[..^".json".Length];
        var dev = package.EndsWith(DevSuffix, StringComparison.Ordinal);
        if (dev)
        {
            package = package[..^DevSuffix.Length];
        }

        var metadata = await _packages.GetMetadataOrNullAsync(
            _feedContext.CurrentFeed, $"{vendor}/{package}", dev, Url.ComposerDists(), cancellationToken);

        return metadata == null ? NotFound() : Content(metadata, JsonContentType);
    }

    // GET composer/dists/{vendor}/{package}/{version}/{reference}.zip
    // The reference is ignored: it only makes the URL change when the zip does.
    public async Task<IActionResult> Dist(string vendor, string package, string version, string file, CancellationToken cancellationToken)
    {
        if (!file.EndsWith(".zip", StringComparison.Ordinal))
        {
            return NotFound();
        }

        var zip = await _packages.GetDistStreamOrNullAsync(_feedContext.CurrentFeed, $"{vendor}/{package}", version, cancellationToken);

        return zip == null ? NotFound() : File(zip, "application/zip");
    }

    // PUT composer/packages/upload/{vendor}/{package}/{version}
    // Composer has no upload; this takes the zip as the body, e.g. from curl -T.
    public async Task<IActionResult> Upload(string vendor, string package, string version, CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;
        var name = $"{vendor}/{package}";

        var denied = await CheckCanPublishAsync(name, version, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        // The server-wide request limit applies before the feed is known; a feed can only lower it.
        var maxBytes = _feedSettings.GetMaxPackageSizeBytes(feed);
        if (Request.ContentLength > maxBytes)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", name, version);
            return TooLarge(maxBytes);
        }

        var sizeLimit = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = maxBytes;
        }

        ComposerUploadResult result;
        try
        {
            result = await _packages.UploadAsync(feed, name, version, Request.Body, maxBytes, cancellationToken);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", name, version);
            return TooLarge(maxBytes);
        }

        switch (result.Status)
        {
            case ComposerUploadStatus.Success:
                await AuditAsync(LogLevel.Information, "package_upload_succeeded", name, version);
                return StatusCode(StatusCodes.Status201Created);

            case ComposerUploadStatus.AlreadyExists:
                await AuditAsync(LogLevel.Warning, "package_upload_already_exists", name, version);
                return Text(StatusCodes.Status409Conflict, result.Message);

            case ComposerUploadStatus.TooLarge:
                await AuditAsync(LogLevel.Warning, "package_upload_too_large", name, version);
                return Text(StatusCodes.Status413PayloadTooLarge, result.Message);

            default:
                await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", name, version);
                return Text(StatusCodes.Status400BadRequest, result.Message);
        }
    }

    /// <summary>
    /// Returns null when the caller may publish to the current feed, or the response to send
    /// otherwise. The caller is already known to be allowed to read the feed.
    /// </summary>
    private async Task<IActionResult> CheckCanPublishAsync(string name, string version, CancellationToken cancellationToken)
    {
        var access = await _changes.CheckAsync(User, Request.GetApiKeyOrPassword(), FeedPermissionRequirement.Push, cancellationToken);
        if (access == FeedChangeAccess.Unauthorized)
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Challenge(AuthenticationConstants.NugetBasicAuthenticationScheme);
            }

            await AuditAsync(LogLevel.Warning, "package_upload_unauthorized", name, version);
            return Text(StatusCodes.Status403Forbidden, "You do not have permission to upload packages to this feed.");
        }

        if (access == FeedChangeAccess.ReadOnly)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_read_only", name, version);
            return Text(StatusCodes.Status403Forbidden, "This feed is read-only.");
        }

        return null;
    }

    private static ContentResult TooLarge(long maxBytes)
    {
        return Text(StatusCodes.Status413PayloadTooLarge, $"The zip is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.");
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
            actor = string.IsNullOrEmpty(Request.GetApiKeyOrPassword()) ? "anonymous" : "api-key";
        }

        await _audit.PackageAsync(HttpContext, level, eventName, _feedContext.CurrentFeed.Slug, packageName, packageVersion, actor);
    }
}
