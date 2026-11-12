using System;
using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Npm;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The npm registry API of npm feeds, used by npm, Yarn and pnpm. It is at <c>npm/</c> under the feed.
/// See: https://github.com/npm/registry/blob/main/docs/REGISTRY-API.md
/// </summary>
[FeedType(FeedType.Npm)]
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class NpmPackageController : Controller
{
    private const long BytesPerMiB = 1024L * 1024;

    // Room in a publish request for the manifest next to the base64 tarball.
    private const long PublishRequestOverheadBytes = 8 * BytesPerMiB;

    private const int MaxSearchResults = 250;

    private readonly INpmPackageService _packages;
    private readonly IArtifactDatabase _artifacts;
    private readonly IFeedContext _feedContext;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IAuthenticationService _authentication;
    private readonly IPermissionService _permissionService;
    private readonly IOptionsSnapshot<PaGettoOptions> _options;
    private readonly WebAuditLog _audit;

    public NpmPackageController(
        INpmPackageService packages,
        IArtifactDatabase artifacts,
        IFeedContext feedContext,
        IFeedSettingsResolver feedSettings,
        IAuthenticationService authentication,
        IPermissionService permissionService,
        IOptionsSnapshot<PaGettoOptions> options,
        WebAuditLog audit)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    // GET npm/-/ping
    public IActionResult Ping()
    {
        return Content("{}", "application/json");
    }

    // GET npm/-/whoami
    public IActionResult WhoAmI()
    {
        return NpmJson(new JsonObject { ["username"] = User.Identity?.Name ?? "anonymous" });
    }

    // GET npm/{name}, npm/@{scope}/{name}
    // npm deprecate and npm unpublish add ?write=true: they get the feed's own versions to change.
    public async Task<IActionResult> GetPackage(string scope, string name, bool write, CancellationToken cancellationToken)
    {
        name = GetPackageName(scope, name);

        var packument = await _packages.GetPackumentOrNullAsync(
            _feedContext.CurrentFeed,
            name,
            localOnly: write,
            (package, version) => Url.NpmTarball(package, version, Request.Scheme),
            cancellationToken);

        return packument == null ? PackageNotFound(name) : NpmJson(packument);
    }

    // GET npm/{name}/-/{fileName}, npm/@{scope}/{name}/-/{fileName}
    public async Task<IActionResult> DownloadTarball(string scope, string name, string fileName, CancellationToken cancellationToken)
    {
        name = GetPackageName(scope, name);

        if (!NpmPackageName.TryGetVersionFromTarballFileName(name, fileName, out var version))
        {
            return NpmError(StatusCodes.Status404NotFound, $"{fileName} is not a tarball of package {name}.");
        }

        var tarball = await _packages.GetTarballStreamOrNullAsync(_feedContext.CurrentFeed, name, version, cancellationToken);
        if (tarball == null)
        {
            return NpmError(StatusCodes.Status404NotFound, $"Version {version} of package {name} was not found.");
        }

        return File(tarball, "application/octet-stream", fileName);
    }

    // PUT npm/{name}, npm/@{scope}/{name} and the same with /-rev/{rev}
    // A document with a tarball in _attachments publishes a version (npm publish). Without one, it
    // is a changed document (npm deprecate, npm unpublish of one version).
    public async Task<IActionResult> PutPackage(string scope, string name, CancellationToken cancellationToken)
    {
        name = GetPackageName(scope, name);
        var feed = _feedContext.CurrentFeed;
        var maxTarballBytes = _feedSettings.GetMaxPackageSizeMiB(feed) * BytesPerMiB;

        var (canPush, canDelete) = await GetWritePermissionsAsync(cancellationToken);
        if (!canPush && !canDelete)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_unauthorized", name, null);
            return NpmError(StatusCodes.Status403Forbidden, "You do not have permission to change packages of this feed.");
        }

        if (_feedSettings.GetIsReadOnlyMode(feed))
        {
            await AuditAsync(LogLevel.Warning, "package_upload_read_only", name, null);
            return ReadOnly();
        }

        // The tarball is sent as base64, which takes a third more room than the tarball itself.
        var maxRequestBytes = maxTarballBytes / 3 * 4 + PublishRequestOverheadBytes;
        if (Request.ContentLength > maxRequestBytes)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", name, null);
            return TooLarge(maxTarballBytes);
        }

        var sizeLimit = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = maxRequestBytes;
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
            var body = document.RootElement;

            var isPublish = body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("_attachments", out var attachments)
                && attachments.ValueKind == JsonValueKind.Object
                && attachments.EnumerateObject().Any();

            if (isPublish)
            {
                if (!canPush)
                {
                    await AuditAsync(LogLevel.Warning, "package_upload_unauthorized", name, null);
                    return NpmError(StatusCodes.Status403Forbidden, "You do not have permission to publish packages to this feed.");
                }

                var published = await _packages.PublishAsync(feed, name, body, maxTarballBytes, cancellationToken);
                await AuditAsync(
                    published.Status == NpmStatus.Success ? LogLevel.Information : LogLevel.Warning,
                    published.Status switch
                    {
                        NpmStatus.Success => "package_upload_succeeded",
                        NpmStatus.AlreadyExists => "package_upload_already_exists",
                        NpmStatus.TooLarge => "package_upload_too_large",
                        _ => "package_upload_invalid_package",
                    },
                    name,
                    published.Version);

                return ToResponse(published, StatusCodes.Status201Created);
            }

            var updated = await _packages.UpdateAsync(feed, name, body, canDeprecate: canPush, canUnpublish: canDelete, cancellationToken);
            await AuditAsync(
                updated.Status == NpmStatus.Success ? LogLevel.Information : LogLevel.Warning,
                updated.Status switch
                {
                    NpmStatus.Success => "package_update_succeeded",
                    NpmStatus.Forbidden => "package_update_unauthorized",
                    NpmStatus.NotFound => "package_update_not_found",
                    _ => "package_update_invalid",
                },
                name,
                null);
            await AuditUnpublishedAsync(name, updated);

            return ToResponse(updated);
        }
        catch (JsonException)
        {
            return NpmError(StatusCodes.Status400BadRequest, "The request body is not valid JSON.");
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", name, null);
            return TooLarge(maxTarballBytes);
        }
    }

    // DELETE npm/{name}/-rev/{rev}, npm/@{scope}/{name}/-rev/{rev}
    // npm unpublish of a whole package.
    public async Task<IActionResult> UnpublishPackage(string scope, string name, CancellationToken cancellationToken)
    {
        name = GetPackageName(scope, name);

        var denied = await CheckCanDeleteAsync(name, null, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        var result = await _packages.UnpublishAsync(_feedContext.CurrentFeed, name, version: null, cancellationToken);
        if (result.Status == NpmStatus.Success)
        {
            await AuditUnpublishedAsync(name, result);
        }
        else
        {
            await AuditAsync(LogLevel.Warning, "package_delete_not_found", name, null);
        }

        return ToResponse(result);
    }

    // DELETE npm/{**path}, for path .../{name}/-/{fileName}/-rev/{rev}
    // The last step of npm unpublish of one version. The version is normally gone already: the
    // step before sent the package document without it.
    public async Task<IActionResult> DeleteTarball(string path, CancellationToken cancellationToken)
    {
        if (!TryParseTarballPath(path, out var name, out var fileName)
            || !NpmPackageName.TryGetVersionFromTarballFileName(name, fileName, out var version))
        {
            return NpmError(StatusCodes.Status404NotFound, "Not found.");
        }

        var denied = await CheckCanDeleteAsync(name, version, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        var result = await _packages.UnpublishAsync(_feedContext.CurrentFeed, name, version, cancellationToken);
        await AuditUnpublishedAsync(name, result);

        return NpmOk();
    }

    // GET npm/-/package/{name}/dist-tags
    public async Task<IActionResult> GetDistTags(string name, CancellationToken cancellationToken)
    {
        name = GetPackageName(null, name);

        var tags = await _packages.GetDistTagsOrNullAsync(_feedContext.CurrentFeed, name, cancellationToken);
        if (tags == null)
        {
            return PackageNotFound(name);
        }

        var json = new JsonObject();
        foreach (var (tag, version) in tags)
        {
            json[tag] = version;
        }

        return NpmJson(json);
    }

    // PUT npm/-/package/{name}/dist-tags/{tag}
    // The body is the version, as a JSON string.
    public async Task<IActionResult> SetDistTag(string name, string tag, CancellationToken cancellationToken)
    {
        name = GetPackageName(null, name);

        var denied = await CheckCanPushAsync(name, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        string version;
        try
        {
            using var document = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
            version = document.RootElement.ValueKind == JsonValueKind.String ? document.RootElement.GetString() : null;
        }
        catch (JsonException)
        {
            version = null;
        }

        if (string.IsNullOrEmpty(version))
        {
            return NpmError(StatusCodes.Status400BadRequest, "The request body must be the version, as a JSON string.");
        }

        var result = await _packages.SetDistTagAsync(_feedContext.CurrentFeed, name, tag, version, cancellationToken);
        await AuditAsync(
            result.Status == NpmStatus.Success ? LogLevel.Information : LogLevel.Warning,
            result.Status == NpmStatus.Success ? "package_tag_succeeded" : "package_tag_failed",
            name,
            version);

        return ToResponse(result);
    }

    // DELETE npm/-/package/{name}/dist-tags/{tag}
    public async Task<IActionResult> RemoveDistTag(string name, string tag, CancellationToken cancellationToken)
    {
        name = GetPackageName(null, name);

        var denied = await CheckCanPushAsync(name, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        var result = await _packages.RemoveDistTagAsync(_feedContext.CurrentFeed, name, tag, cancellationToken);
        await AuditAsync(
            result.Status == NpmStatus.Success ? LogLevel.Information : LogLevel.Warning,
            result.Status == NpmStatus.Success ? "package_untag_succeeded" : "package_untag_failed",
            name,
            null);

        return ToResponse(result);
    }

    // GET npm/-/v1/search?text=&size=&from=
    // Searches the packages stored in the feed: published ones, and mirrored ones once downloaded.
    public async Task<IActionResult> Search(string text, int? size, int? from, CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;
        var take = Math.Clamp(size ?? 20, 1, MaxSearchResults);

        var result = await _artifacts.SearchAsync(feed.Id, text, Math.Max(0, from ?? 0), take, cancellationToken);

        var objects = new JsonArray();
        foreach (var artifact in result.Artifacts)
        {
            var latest = ArtifactVersionOrder.GetLatest(feed.Type, artifact.Versions, artifact.Tags);
            var summary = NpmManifestSummary.Parse(latest.MetadataJson);

            var links = new JsonObject();
            foreach (var (field, url) in summary.Links)
            {
                links[field] = url;
            }

            objects.Add(new JsonObject
            {
                ["package"] = new JsonObject
                {
                    ["name"] = artifact.Name,
                    ["version"] = latest.Version,
                    ["description"] = latest.Description,
                    ["keywords"] = new JsonArray(summary.Keywords.Select(k => (JsonNode)k).ToArray()),
                    ["date"] = DateTime.SpecifyKind(latest.Published, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture),
                    ["links"] = links,

                    // The npm client expects the list, even when the registry has no maintainers.
                    ["maintainers"] = new JsonArray(),
                },
                ["score"] = new JsonObject
                {
                    ["final"] = 1,
                    ["detail"] = new JsonObject { ["quality"] = 1, ["popularity"] = 1, ["maintenance"] = 1 },
                },
                ["searchScore"] = 1,
            });
        }

        return NpmJson(new JsonObject
        {
            ["objects"] = objects,
            ["total"] = result.TotalHits,
            ["time"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        });
    }

    /// <summary>
    /// What the caller may change in the current feed. The caller is already known to be allowed
    /// to read it.
    /// </summary>
    private async Task<(bool CanPush, bool CanDelete)> GetWritePermissionsAsync(CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;
        var authMode = _options.Value.Authentication?.Mode ?? AuthenticationMode.Legacy;

        if (authMode == AuthenticationMode.Legacy)
        {
            // Static auth mode: the configured API key, sent as the npm auth token.
            var authorized = await _authentication.AuthenticateAsync(GetBearerToken(), cancellationToken);
            return (authorized, authorized);
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return (false, false);
        }

        return (
            await _permissionService.CanPushAsync(userId, feed.Id, cancellationToken),
            await _permissionService.CanDeleteAsync(userId, feed.Id, cancellationToken));
    }

    private async Task<IActionResult> CheckCanPushAsync(string name, CancellationToken cancellationToken)
    {
        var (canPush, _) = await GetWritePermissionsAsync(cancellationToken);
        if (!canPush)
        {
            await AuditAsync(LogLevel.Warning, "package_tag_unauthorized", name, null);
            return NpmError(StatusCodes.Status403Forbidden, "You do not have permission to change packages of this feed.");
        }

        if (_feedSettings.GetIsReadOnlyMode(_feedContext.CurrentFeed))
        {
            await AuditAsync(LogLevel.Warning, "package_tag_read_only", name, null);
            return ReadOnly();
        }

        return null;
    }

    private async Task<IActionResult> CheckCanDeleteAsync(string name, string version, CancellationToken cancellationToken)
    {
        var (_, canDelete) = await GetWritePermissionsAsync(cancellationToken);
        if (!canDelete)
        {
            await AuditAsync(LogLevel.Warning, "package_delete_unauthorized", name, version);
            return NpmError(StatusCodes.Status403Forbidden, "You do not have permission to unpublish packages from this feed.");
        }

        if (_feedSettings.GetIsReadOnlyMode(_feedContext.CurrentFeed))
        {
            await AuditAsync(LogLevel.Warning, "package_delete_read_only", name, version);
            return ReadOnly();
        }

        return null;
    }

    /// <summary>
    /// The package name from the route: <c>@{scope}/{name}</c> from the scoped routes, or a name
    /// that is a single segment, where a scoped name is <c>@scope%2fname</c>.
    /// </summary>
    private static string GetPackageName(string scope, string name)
    {
        return scope == null ? NpmPackageName.FromUrlSegment(name) : $"@{scope}/{name}";
    }

    /// <summary>
    /// Reads the package name and tarball file name from <c>.../{name}/-/{fileName}/-rev/{rev}</c>,
    /// where a scoped name is two segments.
    /// </summary>
    private static bool TryParseTarballPath(string path, out string name, out string fileName)
    {
        name = fileName = null;

        var segments = (path ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(NpmPackageName.FromUrlSegment)
            .ToList();

        // name, "-", fileName, "-rev", rev
        if (segments.Count < 5 || segments[^2] != "-rev" || segments[^4] != "-")
        {
            return false;
        }

        fileName = segments[^3];
        name = segments[^5];
        if (segments.Count >= 6 && segments[^6].StartsWith('@') && !name.Contains('/'))
        {
            name = segments[^6] + "/" + name;
        }

        return NpmPackageName.IsValid(name);
    }

    private string GetBearerToken()
    {
        return AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            ? header.Parameter
            : null;
    }

    private static ContentResult ToResponse(NpmResult result, int successStatusCode = StatusCodes.Status200OK)
    {
        return result.Status switch
        {
            NpmStatus.Success => NpmOk(successStatusCode),
            NpmStatus.NotFound => NpmError(StatusCodes.Status404NotFound, result.Message),
            NpmStatus.AlreadyExists => NpmError(StatusCodes.Status409Conflict, result.Message),
            NpmStatus.TooLarge => NpmError(StatusCodes.Status413PayloadTooLarge, result.Message),
            NpmStatus.Forbidden => NpmError(StatusCodes.Status403Forbidden, result.Message),
            _ => NpmError(StatusCodes.Status400BadRequest, result.Message),
        };
    }

    private static ContentResult TooLarge(long maxTarballBytes)
    {
        return NpmError(StatusCodes.Status413PayloadTooLarge, $"The package is larger than the feed's limit of {maxTarballBytes / BytesPerMiB} MiB.");
    }

    private static ContentResult ReadOnly()
    {
        return NpmError(StatusCodes.Status403Forbidden, "This feed is read-only.");
    }

    private static ContentResult PackageNotFound(string name)
    {
        return NpmError(StatusCodes.Status404NotFound, $"Package {name} was not found.");
    }

    private static ContentResult NpmOk(int statusCode = StatusCodes.Status200OK)
    {
        return NpmJson(new JsonObject { ["ok"] = true }, statusCode);
    }

    /// <summary>
    /// An error the npm client shows: it prints the <c>error</c> of the body after the status.
    /// </summary>
    private static ContentResult NpmError(int statusCode, string message)
    {
        return NpmJson(new JsonObject { ["error"] = message }, statusCode);
    }

    private static ContentResult NpmJson(JsonNode json, int statusCode = StatusCodes.Status200OK)
    {
        return new ContentResult
        {
            StatusCode = statusCode,
            ContentType = "application/json",
            Content = json.ToJsonString(),
        };
    }

    /// <summary>
    /// Writes a <c>package_delete_succeeded</c> line for each version a change removed.
    /// </summary>
    private async Task AuditUnpublishedAsync(string packageName, NpmResult result)
    {
        foreach (var version in result.Unpublished)
        {
            await AuditAsync(LogLevel.Information, "package_delete_succeeded", packageName, version);
        }
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
}
