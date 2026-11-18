using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Hex;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;
using PaGetto.Web.Hex;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The Hex repository of Hex feeds at <c>hex/</c> (the signed registry and the tarballs), and the
/// parts of the Hex HTTP API that publish and retire packages at <c>hex/api/</c>. The repository
/// is named after the feed's slug.
/// See: https://github.com/hexpm/specifications/blob/main/endpoints.md and
/// https://github.com/hexpm/specifications/blob/main/apiary.apib
/// </summary>
[FeedType(FeedType.Hex)]
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class HexRepositoryController : Controller
{
    /// <summary>
    /// The media type of the API's bodies: Erlang's external term format.
    /// </summary>
    public const string ErlangContentType = "application/vnd.hex+erlang";

    private const long BytesPerMiB = 1024L * 1024;
    private const int MaxRetireBodyBytes = 64 * 1024;
    private const string RegistryContentType = "application/octet-stream";

    private readonly IHexPackageService _packages;
    private readonly IArtifactDatabase _artifacts;
    private readonly HexRegistryKeyStore _keys;
    private readonly IFeedContext _feedContext;
    private readonly FeedChangeAuthorizer _changes;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly ArtifactIndexCache _indexes;
    private readonly WebAuditLog _audit;

    public HexRepositoryController(
        IHexPackageService packages,
        IArtifactDatabase artifacts,
        HexRegistryKeyStore keys,
        IFeedContext feedContext,
        FeedChangeAuthorizer changes,
        IFeedSettingsResolver feedSettings,
        ArtifactIndexCache indexes,
        WebAuditLog audit)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _indexes = indexes ?? throw new ArgumentNullException(nameof(indexes));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    // GET hex/names
    [ContentETag]
    public async Task<IActionResult> Names(CancellationToken cancellationToken)
    {
        // Only the feed's stored packages are listed, not the mirrors'.
        var feed = _feedContext.CurrentFeed;
        var names = await _indexes.GetOrBuildAsync(feed, "hex/names", ArtifactIndexCache.LocalIndexDuration, async () =>
        {
            var packages = await _packages.FindPackagesAsync(feed, cancellationToken);

            using var key = await _keys.GetPrivateKeyAsync(feed, cancellationToken);
            return HexRegistry.BuildNames(feed.Slug, packages, key);
        });

        return File(names, RegistryContentType);
    }

    // GET hex/versions
    [ContentETag]
    public async Task<IActionResult> Versions(CancellationToken cancellationToken)
    {
        // Only the feed's stored packages are listed, not the mirrors'.
        var feed = _feedContext.CurrentFeed;
        var versions = await _indexes.GetOrBuildAsync(feed, "hex/versions", ArtifactIndexCache.LocalIndexDuration, async () =>
        {
            var packages = await _packages.FindPackagesAsync(feed, cancellationToken);

            using var key = await _keys.GetPrivateKeyAsync(feed, cancellationToken);
            return HexRegistry.BuildVersions(feed.Slug, packages, key);
        });

        return File(versions, RegistryContentType);
    }

    // GET hex/packages/{name}
    [ContentETag]
    public async Task<IActionResult> Package(string name, CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;
        var releases = await _packages.FindReleasesAsync(feed, name, cancellationToken);
        if (releases.Count == 0)
        {
            return NotFound();
        }

        using var key = await _keys.GetPrivateKeyAsync(feed, cancellationToken);
        return File(HexRegistry.BuildPackage(feed.Slug, name, releases, key), RegistryContentType);
    }

    // GET hex/tarballs/{name}-{version}.tar
    public async Task<IActionResult> Tarball(string fileName, CancellationToken cancellationToken)
    {
        // A package name has no '-', so the first one ends it.
        var dash = fileName?.IndexOf('-', StringComparison.Ordinal) ?? -1;
        if (dash <= 0 || !fileName.EndsWith(".tar", StringComparison.Ordinal))
        {
            return NotFound();
        }

        var name = fileName[..dash];
        var version = fileName[(dash + 1)..^4];

        var feed = _feedContext.CurrentFeed;
        var tarball = await _packages.GetTarballStreamOrNullAsync(feed, name, version, cancellationToken);
        if (tarball == null)
        {
            return NotFound();
        }

        // Rebar3 only takes a tarball that has an ETag, which it sends back to check its cache.
        var stored = await _artifacts.FindVersionOrNullAsync(feed.Id, name, version, cancellationToken);
        var etag = stored?.ContentHash == null ? null : new EntityTagHeaderValue($"\"{stored.ContentHash}\"");

        return File(tarball, "application/octet-stream", fileName, lastModified: null, entityTag: etag);
    }

    // GET hex/public_key
    public async Task<IActionResult> PublicKey(CancellationToken cancellationToken)
    {
        return Content(await _keys.GetPublicKeyPemAsync(_feedContext.CurrentFeed, cancellationToken), "text/plain");
    }

    // POST hex/api/packages/{name}/releases?replace={replace}, and POST hex/api/publish of older clients
    // mix hex.publish: the package tarball as the body.
    public async Task<IActionResult> Publish(bool replace, CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;

        var denied = await CheckCanChangeAsync(FeedPermissionRequirement.Push, "package_upload", null, null, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        // The server-wide request limit applies before the feed is known; a feed can only lower it.
        var maxBytes = _feedSettings.GetMaxPackageSizeBytes(feed);
        if (Request.ContentLength > maxBytes)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", null, null);
            return TooLarge(maxBytes);
        }

        var sizeLimit = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = maxBytes;
        }

        HexPublishResult result;
        try
        {
            result = await _packages.PublishAsync(feed, Request.Body, maxBytes, replace, cancellationToken);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", null, null);
            return TooLarge(maxBytes);
        }

        switch (result.Status)
        {
            case HexPublishStatus.Success:
                await AuditAsync(LogLevel.Information, "package_upload_succeeded", result.Name, result.Version);
                return HexTerm(StatusCodes.Status201Created, new Dictionary<string, object>
                {
                    ["version"] = result.Version,
                    ["url"] = $"{Url.HexApi()}/packages/{result.Name}/releases/{Uri.EscapeDataString(result.Version)}",
                    ["html_url"] = Url.Page("/Artifact", pageHandler: null, values: new { id = result.Name, version = result.Version }, protocol: Request.Scheme),
                });

            case HexPublishStatus.AlreadyExists:
                await AuditAsync(LogLevel.Warning, "package_upload_already_exists", result.Name, result.Version);
                return HexError(StatusCodes.Status409Conflict, result.Message);

            case HexPublishStatus.TooLarge:
                await AuditAsync(LogLevel.Warning, "package_upload_too_large", null, null);
                return TooLarge(maxBytes);

            default:
                await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", result.Name, result.Version);
                return HexError(StatusCodes.Status422UnprocessableEntity, result.Message);
        }
    }

    // POST hex/api/packages/{name}/releases/{version}/retire
    // mix hex.retire: the reason and message as a map.
    public async Task<IActionResult> Retire(string name, string version, CancellationToken cancellationToken)
    {
        var denied = await CheckCanChangeAsync(FeedPermissionRequirement.Delete, "package_unlist", name, version, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        var body = await ReadTermOrNullAsync(cancellationToken) as Dictionary<string, object>;
        var reason = body?.GetValueOrDefault("reason") as string;
        if (!HexRetirement.IsValidReason(reason))
        {
            return HexError(StatusCodes.Status422UnprocessableEntity,
                "The reason must be one of other, invalid, security, deprecated and renamed.");
        }

        var retirement = new HexRetirement { Reason = reason, Message = body.GetValueOrDefault("message") as string };
        return await SetRetirementAsync(name, version, retirement, "package_unlist", cancellationToken);
    }

    // DELETE hex/api/packages/{name}/releases/{version}/retire
    public async Task<IActionResult> Unretire(string name, string version, CancellationToken cancellationToken)
    {
        var denied = await CheckCanChangeAsync(FeedPermissionRequirement.Delete, "package_relist", name, version, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        return await SetRetirementAsync(name, version, retirement: null, "package_relist", cancellationToken);
    }

    // GET hex/api/packages/{name}
    // mix hex.publish reads it when a publish is refused, to say whether the package exists.
    public async Task<IActionResult> PackageInfo(string name, CancellationToken cancellationToken)
    {
        var releases = await _packages.FindReleasesAsync(_feedContext.CurrentFeed, name, cancellationToken);
        if (releases.Count == 0)
        {
            return HexError(StatusCodes.Status404NotFound, $"Package {name} was not found.");
        }

        return HexTerm(StatusCodes.Status200OK, new Dictionary<string, object>
        {
            ["name"] = name,
            ["repository"] = _feedContext.CurrentFeed.Slug,
            ["url"] = $"{Url.HexApi()}/packages/{name}",
            ["html_url"] = Url.Page("/Artifact", pageHandler: null, values: new { id = name, version = (string)null }, protocol: Request.Scheme),
            ["releases"] = releases
                .Select(r => new Dictionary<string, object> { ["version"] = r.Version })
                .Reverse()
                .ToList(),
        });
    }

    // GET hex/api/repos/{repository}/packages/{name}
    // mix hex.publish asks hex.pm's public repository whether a package exists before it publishes,
    // without credentials. This feed is not that repository, so the answer is always no.
    [AllowAnonymous]
    public IActionResult OtherRepositoryPackage(string repository)
    {
        return HexError(StatusCodes.Status404NotFound, $"This feed is repository {_feedContext.CurrentFeed.Slug}, not {repository}.");
    }

    // GET hex/api/users/me
    // mix hex.publish reads the user's organizations before it publishes. Feeds have none.
    public IActionResult Me()
    {
        return HexTerm(StatusCodes.Status200OK, new Dictionary<string, object>
        {
            ["username"] = User.Identity?.Name ?? string.Empty,
            ["organizations"] = new List<object>(),
        });
    }

    private async Task<IActionResult> SetRetirementAsync(string name, string version, HexRetirement retirement, string action, CancellationToken cancellationToken)
    {
        if (!await _packages.SetRetirementAsync(_feedContext.CurrentFeed, name, version, retirement, cancellationToken))
        {
            await AuditAsync(LogLevel.Warning, $"{action}_not_found", name, version);
            return HexError(StatusCodes.Status404NotFound, $"Version {version} of package {name} was not found in this feed.");
        }

        await AuditAsync(LogLevel.Information, $"{action}_succeeded", name, version);
        return NoContent();
    }

    /// <summary>
    /// Reads a request body in the external term format.
    /// </summary>
    /// <returns>The term, or null when the body is too large or not a term.</returns>
    private async Task<object> ReadTermOrNullAsync(CancellationToken cancellationToken)
    {
        if (Request.ContentLength > MaxRetireBodyBytes)
        {
            return null;
        }

        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await Request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (body.Length + read > MaxRetireBodyBytes)
            {
                return null;
            }

            body.Write(buffer, 0, read);
        }

        try
        {
            return ErlangExternalTerm.Decode(body.GetBuffer().AsSpan(0, (int)body.Length));
        }
        catch (FormatException)
        {
            return null;
        }
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
        var access = await _changes.CheckAsync(User, GetApiKey(), permission, cancellationToken);
        if (access == FeedChangeAccess.Unauthorized)
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Challenge(AuthenticationConstants.NugetBasicAuthenticationScheme);
            }

            await AuditAsync(LogLevel.Warning, $"{action}_unauthorized", packageName, packageVersion);
            return HexError(StatusCodes.Status403Forbidden, "You do not have permission to change packages of this feed.");
        }

        if (access == FeedChangeAccess.ReadOnly)
        {
            await AuditAsync(LogLevel.Warning, $"{action}_read_only", packageName, packageVersion);
            return HexError(StatusCodes.Status403Forbidden, "This feed is read-only.");
        }

        return null;
    }

    /// <summary>
    /// The API key Hex sends: the whole value of the <c>Authorization</c> header.
    /// </summary>
    private string GetApiKey()
    {
        var value = Request.Headers.Authorization.ToString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static HexTermResult TooLarge(long maxBytes)
    {
        return HexError(StatusCodes.Status413PayloadTooLarge, $"The package is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.");
    }

    /// <summary>
    /// An error Hex shows: the <c>message</c> of the body.
    /// </summary>
    private static HexTermResult HexError(int statusCode, string message)
    {
        return HexTerm(statusCode, new Dictionary<string, object> { ["status"] = statusCode, ["message"] = message });
    }

    private static HexTermResult HexTerm(int statusCode, Dictionary<string, object> body)
    {
        return new HexTermResult(statusCode, ErlangExternalTerm.Encode(body));
    }

    private async Task AuditAsync(LogLevel level, string eventName, string packageName, string packageVersion)
    {
        await _audit.PackageApiAsync(HttpContext, level, eventName, _feedContext.CurrentFeed.Slug, packageName, packageVersion, GetApiKey());
    }

    /// <summary>
    /// A body in the external term format with any status code, which a file result can't have.
    /// </summary>
    private sealed class HexTermResult : IActionResult
    {
        private readonly int _statusCode;
        private readonly byte[] _body;

        public HexTermResult(int statusCode, byte[] body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.StatusCode = _statusCode;
            response.ContentType = ErlangContentType;
            response.ContentLength = _body.Length;
            await response.Body.WriteAsync(_body, context.HttpContext.RequestAborted);
        }
    }
}
