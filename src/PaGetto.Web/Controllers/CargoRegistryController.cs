using System;
using System.IO;
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
using PaGetto.Core.Cargo;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The registry of Cargo feeds: the sparse index at <c>cargo/index/</c> and the registry web API at
/// <c>cargo/api/v1/</c>, which also serves the crates.
/// See: https://doc.rust-lang.org/cargo/reference/registry-index.html and
/// https://doc.rust-lang.org/cargo/reference/registry-web-api.html
/// </summary>
[FeedType(FeedType.Cargo)]
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class CargoRegistryController : Controller
{
    private const long BytesPerMiB = 1024L * 1024;
    private const int MaxSearchResults = 100;

    // The two lengths of a publish request.
    private const long PublishRequestOverheadBytes = 8;

    private readonly ICargoPackageService _packages;
    private readonly IArtifactDatabase _artifacts;
    private readonly IFeedContext _feedContext;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IAuthenticationService _authentication;
    private readonly IPermissionService _permissionService;
    private readonly IOptionsSnapshot<PaGettoOptions> _options;
    private readonly WebAuditLog _audit;

    public CargoRegistryController(
        ICargoPackageService packages,
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

    // GET cargo/index/config.json
    public IActionResult Config()
    {
        // Cargo reads this first. With user accounts, an anonymous request is challenged before it
        // gets here, and Cargo then sends its token with every request, as auth-required asks.
        // Only a server open to anonymous callers lets Cargo go without a token.
        var isAnonymous = User.HasClaim(c => c.Type == ClaimTypes.Anonymous);

        return CargoJson(new JsonObject
        {
            ["dl"] = Url.CargoDownloads(),
            ["api"] = Url.CargoApi(),
            ["auth-required"] = !isAnonymous,
        });
    }

    // GET cargo/index/{**path}
    // A crate's index file, at a path made from its lowercased name: 1/a, 2/ab, 3/a/abc, ab/cd/abcd.
    public async Task<IActionResult> Index(string path, CancellationToken cancellationToken)
    {
        var name = path?[(path.LastIndexOf('/') + 1)..];
        if (!CargoPackageName.IsValid(name) || path != CargoPackageName.GetIndexPath(name))
        {
            return NotFound();
        }

        var versions = await _packages.FindVersionsAsync(_feedContext.CurrentFeed, name, cancellationToken);
        if (versions.Count == 0)
        {
            return NotFound();
        }

        // One JSON object per line.
        using var content = new MemoryStream();
        foreach (var version in versions)
        {
            using (var writer = new Utf8JsonWriter(content))
            {
                CargoIndexEntry.Write(writer, version.ManifestJson, version.Cksum, version.Yanked);
            }

            content.WriteByte((byte)'\n');
        }

        return File(content.ToArray(), "text/plain; charset=utf-8");
    }

    // GET cargo/api/v1/crates/{name}/{version}/download
    public async Task<IActionResult> Download(string name, string version, CancellationToken cancellationToken)
    {
        var crate = await _packages.GetCrateStreamOrNullAsync(_feedContext.CurrentFeed, name, version, cancellationToken);
        if (crate == null)
        {
            return CargoError(StatusCodes.Status404NotFound, $"Version {version} of crate {name} was not found.");
        }

        return File(crate, "application/gzip", $"{name}-{version}.crate");
    }

    // PUT cargo/api/v1/crates/new
    // cargo publish: the metadata and the .crate file in one binary body.
    public async Task<IActionResult> Publish(CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;

        var denied = await CheckCanChangeAsync(FeedPermissionRequirement.Push, "package_upload", null, null, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        // The server-wide request limit applies before the feed is known; a feed can only lower it.
        var maxCrateBytes = _feedSettings.GetMaxPackageSizeMiB(feed) * BytesPerMiB;
        var maxRequestBytes = maxCrateBytes + CargoPublishRequest.MaxMetadataBytes + PublishRequestOverheadBytes;
        if (Request.ContentLength > maxRequestBytes)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", null, null);
            return TooLarge(maxCrateBytes);
        }

        var sizeLimit = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = maxRequestBytes;
        }

        CargoPublishRequest request;
        try
        {
            request = await CargoPublishRequest.ReadAsync(Request.Body, maxCrateBytes, cancellationToken);
        }
        catch (Exception e) when (e is ArtifactFileTooLargeException
            || (e is BadHttpRequestException bad && bad.StatusCode == StatusCodes.Status413PayloadTooLarge))
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", null, null);
            return TooLarge(maxCrateBytes);
        }
        catch (InvalidCargoPackageException e)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", null, null);
            return CargoError(StatusCodes.Status400BadRequest, e.Message);
        }

        CargoPublishResult result;
        using (request)
        {
            result = await _packages.PublishAsync(feed, request, cancellationToken);
        }

        switch (result.Status)
        {
            case CargoPublishStatus.Success:
                await AuditAsync(LogLevel.Information, "package_upload_succeeded", result.Name, result.Version);
                return CargoJson(new JsonObject
                {
                    ["warnings"] = new JsonObject
                    {
                        ["invalid_categories"] = new JsonArray(),
                        ["invalid_badges"] = new JsonArray(),
                        ["other"] = new JsonArray(),
                    },
                });

            case CargoPublishStatus.AlreadyExists:
                await AuditAsync(LogLevel.Warning, "package_upload_already_exists", result.Name, result.Version);
                return CargoError(StatusCodes.Status409Conflict, result.Message);

            default:
                await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", result.Name, result.Version);
                return CargoError(StatusCodes.Status400BadRequest, result.Message);
        }
    }

    // DELETE cargo/api/v1/crates/{name}/{version}/yank
    public Task<IActionResult> Yank(string name, string version, CancellationToken cancellationToken)
    {
        return SetYankedAsync(name, version, yanked: true, cancellationToken);
    }

    // PUT cargo/api/v1/crates/{name}/{version}/unyank
    public Task<IActionResult> Unyank(string name, string version, CancellationToken cancellationToken)
    {
        return SetYankedAsync(name, version, yanked: false, cancellationToken);
    }

    // GET cargo/api/v1/crates?q={query}&per_page={count}
    // cargo search: the crates stored in the feed, published ones and mirrored ones once downloaded.
    public async Task<IActionResult> Search(string q, [FromQuery(Name = "per_page")] int? perPage, CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;
        var result = await _artifacts.SearchAsync(feed.Id, q, 0, Math.Clamp(perPage ?? 10, 1, MaxSearchResults), cancellationToken);

        var crates = new JsonArray();
        foreach (var artifact in result.Artifacts)
        {
            var latest = ArtifactVersionOrder.GetLatest(feed.Type, artifact.Versions);

            crates.Add(new JsonObject
            {
                ["name"] = CargoManifestSummary.Parse(latest.MetadataJson).Name ?? artifact.Name,
                ["max_version"] = latest.Version,
                ["description"] = latest.Description,
            });
        }

        return CargoJson(new JsonObject
        {
            ["crates"] = crates,
            ["meta"] = new JsonObject { ["total"] = result.TotalHits },
        });
    }

    private async Task<IActionResult> SetYankedAsync(string name, string version, bool yanked, CancellationToken cancellationToken)
    {
        var action = yanked ? "package_unlist" : "package_relist";

        // Yanking is unlisting, which takes the delete permission, as on the package page.
        var denied = await CheckCanChangeAsync(FeedPermissionRequirement.Delete, action, name, version, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        if (!await _packages.SetYankedAsync(_feedContext.CurrentFeed, name, version, yanked, cancellationToken))
        {
            await AuditAsync(LogLevel.Warning, $"{action}_not_found", name, version);
            return CargoError(StatusCodes.Status404NotFound, $"Version {version} of crate {name} was not found in this feed.");
        }

        await AuditAsync(LogLevel.Information, $"{action}_succeeded", name, version);
        return CargoJson(new JsonObject { ["ok"] = true });
    }

    /// <summary>
    /// Returns null when the caller may change the current feed with the given permission, or the
    /// response to send otherwise. The caller is already known to be allowed to read the feed.
    /// </summary>
    private async Task<IActionResult> CheckCanChangeAsync(
        string permission,
        string action,
        string packageName,
        string packageVersion,
        CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;
        var authMode = _options.Value.Authentication?.Mode ?? AuthenticationMode.Legacy;

        bool authorized;
        if (authMode == AuthenticationMode.Legacy)
        {
            // Static auth mode: the configured API key, sent as the Cargo token.
            authorized = await _authentication.AuthenticateAsync(GetApiKey(), cancellationToken);
        }
        else
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            authorized = Guid.TryParse(userIdClaim, out var userId)
                && (permission == FeedPermissionRequirement.Push
                    ? await _permissionService.CanPushAsync(userId, feed.Id, cancellationToken)
                    : await _permissionService.CanDeleteAsync(userId, feed.Id, cancellationToken));
        }

        if (!authorized)
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Challenge(AuthenticationConstants.NugetBasicAuthenticationScheme);
            }

            await AuditAsync(LogLevel.Warning, $"{action}_unauthorized", packageName, packageVersion);
            return CargoError(StatusCodes.Status403Forbidden, "You do not have permission to change crates of this feed.");
        }

        if (_feedSettings.GetIsReadOnlyMode(feed))
        {
            await AuditAsync(LogLevel.Warning, $"{action}_read_only", packageName, packageVersion);
            return CargoError(StatusCodes.Status403Forbidden, "This feed is read-only.");
        }

        return null;
    }

    /// <summary>
    /// The token Cargo sends: the whole value of the <c>Authorization</c> header.
    /// </summary>
    private string GetApiKey()
    {
        var value = Request.Headers.Authorization.ToString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static ContentResult TooLarge(long maxCrateBytes)
    {
        return CargoError(StatusCodes.Status413PayloadTooLarge, $"The crate is larger than the feed's limit of {maxCrateBytes / BytesPerMiB} MiB.");
    }

    /// <summary>
    /// An error Cargo shows: the <c>detail</c> of each of the body's <c>errors</c>.
    /// </summary>
    private static ContentResult CargoError(int statusCode, string message)
    {
        return CargoJson(new JsonObject { ["errors"] = new JsonArray(new JsonObject { ["detail"] = message }) }, statusCode);
    }

    private static ContentResult CargoJson(JsonNode json, int statusCode = StatusCodes.Status200OK)
    {
        return new ContentResult
        {
            StatusCode = statusCode,
            ContentType = "application/json",
            Content = json.ToJsonString(),
        };
    }

    private async Task AuditAsync(LogLevel level, string eventName, string packageName, string packageVersion)
    {
        var actor = User.Identity?.Name;
        if (string.IsNullOrEmpty(actor))
        {
            // Config mode API keys are shared and carry no user identity.
            actor = GetApiKey() == null ? "anonymous" : "api-key";
        }

        await _audit.PackageAsync(HttpContext, level, eventName, _feedContext.CurrentFeed.Slug, packageName, packageVersion, actor);
    }
}
