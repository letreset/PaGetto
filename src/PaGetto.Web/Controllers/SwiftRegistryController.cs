using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Swift;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The Swift package registry API of Swift feeds, at <c>swift/</c> under the feed, which Swift
/// Package Manager reads and publishes to.
/// See: https://github.com/swiftlang/swift-package-manager/blob/main/Documentation/PackageRegistry/Registry.md
/// </summary>
[FeedType(FeedType.Swift)]
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public partial class SwiftRegistryController : Controller
{
    private const long BytesPerMiB = 1024L * 1024;

    // What a publish may have besides the source archive: the metadata, the signatures and the framing.
    private const long MaxPartBytes = 16 * BytesPerMiB;

    private const string JsonContentType = "application/json";
    private const string ProblemContentType = "application/problem+json";
    private const string SwiftContentType = "text/x-swift";
    private const string ZipContentType = "application/zip";

    private const string SignatureFormatHeader = "X-Swift-Package-Signature-Format";
    private const string SignatureHeader = "X-Swift-Package-Signature";

    private readonly ISwiftPackageService _packages;
    private readonly IFeedContext _feedContext;
    private readonly FeedChangeAuthorizer _changes;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IAuthenticationService _authentication;
    private readonly IOptionsSnapshot<PaGettoOptions> _options;
    private readonly WebAuditLog _audit;

    public SwiftRegistryController(
        ISwiftPackageService packages,
        IFeedContext feedContext,
        FeedChangeAuthorizer changes,
        IFeedSettingsResolver feedSettings,
        IAuthenticationService authentication,
        IOptionsSnapshot<PaGettoOptions> options,
        WebAuditLog audit)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <summary>
    /// Every answer of the API names its version, and a request for another version is refused.
    /// </summary>
    [NonAction]
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        Response.Headers["Content-Version"] = "1";

        var requested = Request.Headers.Accept
            .SelectMany(value => (value ?? "").Split(','))
            .Select(type => ApiMediaTypeRegex().Match(type.Split(';')[0].Trim()))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .ToList();

        if (requested.Count > 0 && !requested.Contains("1"))
        {
            context.Result = ProblemDetail(StatusCodes.Status415UnsupportedMediaType, "This registry only supports version 1 of the API.");
        }
    }

    // GET swift/{scope}/{name} and swift/{scope}/{name}.json
    public async Task<IActionResult> ListReleases(string scope, string name, CancellationToken cancellationToken)
    {
        if (name.EndsWith(".json", StringComparison.Ordinal))
        {
            name = name[..^".json".Length];
        }

        var list = await _packages.ListReleasesOrNullAsync(_feedContext.CurrentFeed, scope, name, cancellationToken);
        if (list == null)
        {
            return ProblemDetail(StatusCodes.Status404NotFound, $"Package {scope}.{name} was not found.");
        }

        var releases = new JsonObject();
        foreach (var version in list.Versions)
        {
            releases[version] = new JsonObject { ["url"] = Url.SwiftRelease(scope, name, version) };
        }

        return JsonText(new JsonObject { ["releases"] = releases });
    }

    // GET swift/{scope}/{name}/{version}, swift/{scope}/{name}/{version}.json and swift/{scope}/{name}/{version}.zip
    public async Task<IActionResult> Release(string scope, string name, string version, CancellationToken cancellationToken)
    {
        var archive = version.EndsWith(".zip", StringComparison.Ordinal);
        if (archive)
        {
            version = version[..^".zip".Length];
        }
        else if (version.EndsWith(".json", StringComparison.Ordinal) && SwiftVersion.TryParse(version[..^".json".Length], out _))
        {
            version = version[..^".json".Length];
        }

        var feed = _feedContext.CurrentFeed;
        var release = await _packages.GetReleaseOrNullAsync(feed, scope, name, version, cancellationToken);
        if (release == null)
        {
            return ReleaseNotFound(scope, name, version);
        }

        if (!archive)
        {
            return JsonText(release.Manifest.ToReleaseJson(release.Version.ContentHash));
        }

        Response.Headers.ContentDisposition = $"attachment; filename=\"{name}-{release.Version.Version}.zip\"";
        Response.Headers["Digest"] = "sha-256=" + Convert.ToBase64String(Convert.FromHexString(release.Version.ContentHash));
        if (release.Manifest.Signature != null)
        {
            Response.Headers[SignatureFormatHeader] = release.Manifest.SignatureFormat;
            Response.Headers[SignatureHeader] = release.Manifest.Signature;
        }

        return File(await _packages.GetArchiveStreamAsync(feed, release, cancellationToken), ZipContentType);
    }

    // GET swift/{scope}/{name}/{version}/Package.swift?swift-version=
    public async Task<IActionResult> Manifest(
        string scope,
        string name,
        string version,
        [FromQuery(Name = "swift-version")] string swiftVersion,
        CancellationToken cancellationToken)
    {
        var release = await _packages.GetReleaseOrNullAsync(_feedContext.CurrentFeed, scope, name, version, cancellationToken);
        if (release == null)
        {
            return ReleaseNotFound(scope, name, version);
        }

        var manifestUrl = Url.SwiftRelease(scope, name, release.Version.Version) + "/" + SwiftManifest.ManifestFileName;
        var fileName = SwiftManifest.ManifestFileName;

        if (!string.IsNullOrEmpty(swiftVersion))
        {
            fileName = release.Manifest.FindVersionSpecificManifest(swiftVersion);
            if (fileName == null)
            {
                // The package has no manifest for that Swift version, so the client takes the one for all.
                Response.Headers.Location = manifestUrl;
                return StatusCode(StatusCodes.Status303SeeOther);
            }
        }
        else
        {
            var alternates = release.Manifest.GetAlternateManifests()
                .Select(a => $"<{manifestUrl}?swift-version={a.SwiftVersion}>; rel=\"alternate\"; filename=\"{a.FileName}\"; swift-tools-version=\"{a.ToolsVersion}\"")
                .ToList();

            if (alternates.Count > 0)
            {
                Response.Headers.Link = string.Join(", ", alternates);
            }
        }

        if (!release.Manifest.Manifests.TryGetValue(fileName, out var content))
        {
            return ReleaseNotFound(scope, name, version);
        }

        Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
        return File(Encoding.UTF8.GetBytes(content), SwiftContentType);
    }

    // GET swift/identifiers?url=
    public async Task<IActionResult> Identifiers([FromQuery] string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return ProblemDetail(StatusCodes.Status400BadRequest, "The url query parameter names the repository URL to look up.");
        }

        var identifiers = await _packages.LookupIdentifiersAsync(_feedContext.CurrentFeed, url, cancellationToken);
        if (identifiers.Count == 0)
        {
            return ProblemDetail(StatusCodes.Status404NotFound, $"No package of this registry has the repository URL {url}.");
        }

        return JsonText(new JsonObject { ["identifiers"] = new JsonArray(identifiers.Select(i => (JsonNode)i).ToArray()) });
    }

    // POST swift/login and swift, which swift package-registry login calls to check the credentials.
    public async Task<IActionResult> Login(CancellationToken cancellationToken)
    {
        // With user accounts, the caller is signed in to get here. A Legacy server checks the API key.
        var authMode = _options.Value.Authentication?.Mode ?? AuthenticationMode.Legacy;
        if (authMode == AuthenticationMode.Legacy && !await _authentication.AuthenticateAsync(Request.GetApiKeyOrPassword(), cancellationToken))
        {
            return Challenge(AuthenticationConstants.NugetBasicAuthenticationScheme);
        }

        return Ok();
    }

    // PUT swift/{scope}/{name}/{version}
    // A multipart form with the source archive as "source-archive", and optionally the release
    // metadata as "metadata" and the signature of the archive as "source-archive-signature", whose
    // format is in the X-Swift-Package-Signature-Format header.
    // The action has no parameters, so model binding doesn't run: its form value provider would
    // read the body as a form first, and Swift Package Manager sends the parts without file names,
    // which a form keeps as text.
    public async Task<IActionResult> Publish()
    {
        var scope = (string)RouteData.Values["scope"];
        var name = (string)RouteData.Values["name"];
        var version = (string)RouteData.Values["version"];
        var cancellationToken = HttpContext.RequestAborted;

        var feed = _feedContext.CurrentFeed;
        var id = $"{scope}.{name}";

        var denied = await CheckCanPublishAsync(id, version, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        // The server-wide request limit applies before the feed is known; a feed can only lower it.
        var maxBytes = _feedSettings.GetMaxPackageSizeBytes(feed);
        if (Request.ContentLength > maxBytes + MaxPartBytes)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", id, version);
            return TooLarge(maxBytes);
        }

        var sizeLimit = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = maxBytes + MaxPartBytes;
        }

        if (!Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType)
            || !contentType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(contentType.Boundary.Value))
        {
            await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", id, version);
            return ProblemDetail(StatusCodes.Status400BadRequest, "The release must be published as multipart/form-data with the source archive as 'source-archive'.");
        }

        ArtifactFileContent archive = null;
        try
        {
            var parts = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var reader = new MultipartReader(Microsoft.Net.Http.Headers.HeaderUtilities.RemoveQuotes(contentType.Boundary).Value, Request.Body);
                while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
                {
                    var partName = GetPartName(section);
                    if (partName == "source-archive" && archive == null)
                    {
                        archive = await ArtifactFileContent.ReadAsync(section.Body, maxBytes, cancellationToken);
                    }
                    else if (partName != null && !parts.ContainsKey(partName))
                    {
                        parts[partName] = await ReadPartAsync(section.Body, partName, cancellationToken);
                    }
                }
            }
            catch (Exception e) when (e is ArtifactFileTooLargeException
                || (e is BadHttpRequestException bad && bad.StatusCode == StatusCodes.Status413PayloadTooLarge))
            {
                await AuditAsync(LogLevel.Warning, "package_upload_too_large", id, version);
                return TooLarge(maxBytes);
            }
            catch (IOException)
            {
                // A body that ends before its last boundary.
                await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", id, version);
                return ProblemDetail(StatusCodes.Status400BadRequest, "The multipart body is incomplete.");
            }

            if (archive == null)
            {
                await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", id, version);
                return ProblemDetail(StatusCodes.Status422UnprocessableEntity, "The release has no source archive in its 'source-archive' part.");
            }

            var signature = parts.GetValueOrDefault("source-archive-signature");
            var result = await _packages.PublishAsync(
                feed,
                scope,
                name,
                version,
                archive.Stream,
                parts.GetValueOrDefault("metadata"),
                string.IsNullOrEmpty(signature) ? null : signature,
                Request.Headers[SignatureFormatHeader].FirstOrDefault(),
                maxBytes,
                cancellationToken);

            switch (result.Status)
            {
                case SwiftPublishStatus.Success:
                    await AuditAsync(LogLevel.Information, "package_upload_succeeded", id, version);
                    Response.Headers.Location = Url.SwiftRelease(scope, name, version);
                    return new ContentResult
                    {
                        StatusCode = StatusCodes.Status201Created,
                        ContentType = JsonContentType,
                        Content = new JsonObject { ["message"] = $"Published {id} {version}." }.ToJsonString(),
                    };

                case SwiftPublishStatus.AlreadyExists:
                    await AuditAsync(LogLevel.Warning, "package_upload_already_exists", id, version);
                    return ProblemDetail(StatusCodes.Status409Conflict, result.Message);

                case SwiftPublishStatus.TooLarge:
                    await AuditAsync(LogLevel.Warning, "package_upload_too_large", id, version);
                    return ProblemDetail(StatusCodes.Status413PayloadTooLarge, result.Message);

                case SwiftPublishStatus.InvalidRequest:
                    await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", id, version);
                    return ProblemDetail(StatusCodes.Status400BadRequest, result.Message);

                default:
                    await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", id, version);
                    return ProblemDetail(StatusCodes.Status422UnprocessableEntity, result.Message);
            }
        }
        finally
        {
            archive?.Dispose();
        }
    }

    /// <summary>
    /// Reads a part of a publish other than the source archive: the metadata as JSON text, and a
    /// signature, which is binary, as base64.
    /// </summary>
    private static async Task<string> ReadPartAsync(Stream body, string partName, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxPartBytes)
            {
                throw new ArtifactFileTooLargeException(MaxPartBytes);
            }

            buffer.Write(chunk, 0, read);
        }

        return partName.EndsWith("-signature", StringComparison.Ordinal)
            ? Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length)
            : Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static string GetPartName(MultipartSection section)
    {
        return Microsoft.Net.Http.Headers.ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
            ? Microsoft.Net.Http.Headers.HeaderUtilities.RemoveQuotes(disposition.Name).Value
            : null;
    }

    /// <summary>
    /// Returns null when the caller may publish to the current feed, or the response to send
    /// otherwise. The caller is already known to be allowed to read the feed.
    /// </summary>
    private async Task<IActionResult> CheckCanPublishAsync(string id, string version, CancellationToken cancellationToken)
    {
        var access = await _changes.CheckAsync(User, Request.GetApiKeyOrPassword(), FeedPermissionRequirement.Push, cancellationToken);
        if (access == FeedChangeAccess.Unauthorized)
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Challenge(AuthenticationConstants.NugetBasicAuthenticationScheme);
            }

            await AuditAsync(LogLevel.Warning, "package_upload_unauthorized", id, version);
            return ProblemDetail(StatusCodes.Status403Forbidden, "You do not have permission to publish packages to this feed.");
        }

        if (access == FeedChangeAccess.ReadOnly)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_read_only", id, version);
            return ProblemDetail(StatusCodes.Status403Forbidden, "This feed is read-only.");
        }

        return null;
    }

    private static ContentResult ReleaseNotFound(string scope, string name, string version)
    {
        return ProblemDetail(StatusCodes.Status404NotFound, $"Version {version} of package {scope}.{name} was not found.");
    }

    private static ContentResult TooLarge(long maxBytes)
    {
        return ProblemDetail(StatusCodes.Status413PayloadTooLarge, $"The source archive is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.");
    }

    private static ContentResult JsonText(JsonNode json)
    {
        return JsonText(json.ToJsonString());
    }

    private static ContentResult JsonText(string json)
    {
        return new ContentResult { StatusCode = StatusCodes.Status200OK, ContentType = JsonContentType, Content = json };
    }

    /// <summary>
    /// An error as problem details, whose detail Swift Package Manager shows.
    /// </summary>
    private static ContentResult ProblemDetail(int statusCode, string detail)
    {
        return new ContentResult
        {
            StatusCode = statusCode,
            ContentType = ProblemContentType,
            Content = new JsonObject { ["detail"] = detail }.ToJsonString(),
        };
    }

    private async Task AuditAsync(LogLevel level, string eventName, string packageName, string packageVersion)
    {
        await _audit.PackageApiAsync(HttpContext, level, eventName, _feedContext.CurrentFeed.Slug, packageName, packageVersion, Request.GetApiKeyOrPassword());
    }

    [GeneratedRegex(@"\Aapplication/vnd\.swift\.registry\.v(\d+)(?:\+[a-z]+)?\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ApiMediaTypeRegex();
}
