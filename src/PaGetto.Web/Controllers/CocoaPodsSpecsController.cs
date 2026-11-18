using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Authentication;
using PaGetto.Core.CocoaPods;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The spec repository of CocoaPods feeds, at <c>cocoapods/</c> under the feed: a CocoaPods spec
/// repository in the CDN format, which CocoaPods reads, the source archives published with podspecs,
/// and a publish endpoint, which CocoaPods has no protocol for (<c>pod repo push</c> pushes to git
/// repositories, <c>pod trunk push</c> only to the public trunk).
/// See: https://github.com/CocoaPods/Core/blob/master/lib/cocoapods-core/cdn_source.rb
/// </summary>
[FeedType(FeedType.CocoaPods)]
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class CocoaPodsSpecsController : Controller
{
    private const long BytesPerMiB = 1024L * 1024;

    // What a publish may have besides the source archive: the podspec and the form's framing.
    private const long MaxPodspecBytes = 16 * BytesPerMiB;

    private const string TextContentType = "text/plain; charset=utf-8";
    private const string JsonContentType = "application/json";

    private readonly ICocoaPodsPackageService _pods;
    private readonly IFeedContext _feedContext;
    private readonly FeedChangeAuthorizer _changes;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly ArtifactIndexCache _indexes;
    private readonly WebAuditLog _audit;

    public CocoaPodsSpecsController(
        ICocoaPodsPackageService pods,
        IFeedContext feedContext,
        FeedChangeAuthorizer changes,
        IFeedSettingsResolver feedSettings,
        ArtifactIndexCache indexes,
        WebAuditLog audit)
    {
        _pods = pods ?? throw new ArgumentNullException(nameof(pods));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _indexes = indexes ?? throw new ArgumentNullException(nameof(indexes));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    // GET cocoapods/CocoaPods-version.yml, all_pods.txt, deprecated_podspecs.txt and
    // all_pods_versions_{a}_{b}_{c}.txt
    public async Task<IActionResult> RepositoryFile(string file, CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;

        string content;
        if (file == CocoaPodsSpecRepository.VersionFileName)
        {
            content = CocoaPodsSpecRepository.VersionFile;
        }
        else if (file == CocoaPodsSpecRepository.AllPodsFileName)
        {
            content = await _indexes.GetOrBuildAsync(
                feed,
                "cocoapods/" + file,
                ArtifactIndexCache.LocalIndexDuration,
                () => _pods.GetAllPodsAsync(feed, cancellationToken),
                text => text.Length * sizeof(char));
        }
        else if (file == CocoaPodsSpecRepository.DeprecatedPodspecsFileName)
        {
            // CocoaPods reads this file on pod repo update and fails when it is missing. Deprecations
            // are in the podspecs, which CocoaPods downloads anyway.
            content = "";
        }
        else if (CocoaPodsSpecRepository.TryParseIndexFileName(file, out var shard))
        {
            // CocoaPods asks for the shard of each pod it resolves, and a shard is merged with the mirrors'.
            content = await _indexes.GetOrBuildAsync(
                feed,
                "cocoapods/" + file,
                ArtifactIndexCache.GetMirroredIndexDuration(_feedSettings, feed),
                () => _pods.GetIndexAsync(feed, shard, cancellationToken),
                text => text.Length * sizeof(char));
        }
        else
        {
            return NotFound();
        }

        return WithETag(content, TextContentType);
    }

    // GET cocoapods/Specs/{a}/{b}/{c}/{name}/{version}/{name}.podspec.json
    public async Task<IActionResult> Podspec(string a, string b, string c, string name, string version, string file, CancellationToken cancellationToken)
    {
        if (!CocoaPodsPodName.IsValid(name)
            || file != name + CocoaPodsSpecRepository.PodspecExtension
            || !CocoaPodsPodName.GetShard(name).SequenceEqual([a, b, c]))
        {
            return NotFound();
        }

        var podspec = await _pods.GetPodspecOrNullAsync(_feedContext.CurrentFeed, name, version, Url.CocoaPodsSources(), cancellationToken);

        return podspec == null ? NotFound() : WithETag(podspec, JsonContentType);
    }

    // GET cocoapods/sources/{name}/{version}/{file}
    public async Task<IActionResult> Source(string name, string version, string file, CancellationToken cancellationToken)
    {
        var archive = await _pods.GetSourceStreamOrNullAsync(_feedContext.CurrentFeed, name, version, file, cancellationToken);

        return archive == null ? NotFound() : File(archive, "application/octet-stream");
    }

    // POST cocoapods/api/v1/pods
    // A multipart form with the podspec in its JSON form as "podspec", and optionally the source
    // archive as "source", e.g. from curl -F podspec=@MyLib.podspec.json -F source=@MyLib.zip.
    public async Task<IActionResult> Publish(CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;

        var denied = await CheckCanPublishAsync(cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        // The server-wide request limit applies before the feed is known; a feed can only lower it.
        var maxBytes = _feedSettings.GetMaxPackageSizeBytes(feed);
        if (Request.ContentLength > maxBytes + MaxPodspecBytes)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", null, null);
            return TooLarge(maxBytes);
        }

        var sizeLimit = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = maxBytes + MaxPodspecBytes;
        }

        if (!Request.HasFormContentType)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", null, null);
            return Text(StatusCodes.Status400BadRequest, "The upload must be a multipart form with the podspec as 'podspec' and optionally the source archive as 'source'.");
        }

        IFormCollection form;
        try
        {
            form = await Request.ReadFormAsync(cancellationToken);
        }
        catch (Exception e) when (e is InvalidDataException
            || (e is BadHttpRequestException bad && bad.StatusCode == StatusCodes.Status413PayloadTooLarge))
        {
            // InvalidDataException is a form part over its limit.
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", null, null);
            return TooLarge(maxBytes);
        }

        string podspec;
        var podspecFile = form.Files.GetFile("podspec");
        if (podspecFile != null)
        {
            if (podspecFile.Length > MaxPodspecBytes)
            {
                await AuditAsync(LogLevel.Warning, "package_upload_too_large", null, null);
                return Text(StatusCodes.Status413PayloadTooLarge, $"The podspec is larger than {MaxPodspecBytes / BytesPerMiB} MiB.");
            }

            using var reader = new StreamReader(podspecFile.OpenReadStream());
            podspec = await reader.ReadToEndAsync(cancellationToken);
        }
        else
        {
            podspec = form["podspec"].ToString();
        }

        if (string.IsNullOrWhiteSpace(podspec))
        {
            await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", null, null);
            return Text(StatusCodes.Status400BadRequest, "The upload has no podspec in its 'podspec' field.");
        }

        var sourceFile = form.Files.GetFile("source");
        CocoaPodsPublishResult result;
        await using (var source = sourceFile?.OpenReadStream())
        {
            result = await _pods.PublishAsync(feed, podspec, source, Path.GetFileName(sourceFile?.FileName), maxBytes, cancellationToken);
        }

        switch (result.Status)
        {
            case CocoaPodsPublishStatus.Success:
                await AuditAsync(LogLevel.Information, "package_upload_succeeded", result.Name, result.Version);
                return Text(StatusCodes.Status201Created, $"Published {result.Name} {result.Version}.");

            case CocoaPodsPublishStatus.AlreadyExists:
                await AuditAsync(LogLevel.Warning, "package_upload_already_exists", result.Name, result.Version);
                return Text(StatusCodes.Status409Conflict, result.Message);

            case CocoaPodsPublishStatus.TooLarge:
                await AuditAsync(LogLevel.Warning, "package_upload_too_large", result.Name, result.Version);
                return Text(StatusCodes.Status413PayloadTooLarge, result.Message);

            default:
                await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", result.Name, result.Version);
                return Text(StatusCodes.Status400BadRequest, result.Message);
        }
    }

    /// <summary>
    /// A text response with an ETag of its content. CocoaPods keeps the ETags of the files it
    /// downloads and sends them back on pod repo update, so unchanged files answer 304.
    /// </summary>
    private IActionResult WithETag(string content, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var etag = $"\"{Convert.ToHexStringLower(SHA256.HashData(bytes))[..32]}\"";

        Response.Headers.ETag = etag;
        if (Request.Headers.IfNoneMatch.Any(value => value == etag))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return File(bytes, contentType);
    }

    /// <summary>
    /// Returns null when the caller may publish to the current feed, or the response to send
    /// otherwise. The caller is already known to be allowed to read the feed.
    /// </summary>
    private async Task<IActionResult> CheckCanPublishAsync(CancellationToken cancellationToken)
    {
        var access = await _changes.CheckAsync(User, Request.GetApiKeyOrPassword(), FeedPermissionRequirement.Push, cancellationToken);
        if (access == FeedChangeAccess.Unauthorized)
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Challenge(AuthenticationConstants.NugetBasicAuthenticationScheme);
            }

            await AuditAsync(LogLevel.Warning, "package_upload_unauthorized", null, null);
            return Text(StatusCodes.Status403Forbidden, "You do not have permission to publish pods to this feed.");
        }

        if (access == FeedChangeAccess.ReadOnly)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_read_only", null, null);
            return Text(StatusCodes.Status403Forbidden, "This feed is read-only.");
        }

        return null;
    }

    private static ContentResult TooLarge(long maxBytes)
    {
        return Text(StatusCodes.Status413PayloadTooLarge, $"The source archive is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.");
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
