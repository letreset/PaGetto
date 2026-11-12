using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.PyPI;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The package index of PyPI feeds, used by pip, uv, Poetry and twine. It is at <c>pypi/</c> under
/// the feed: the simple repository API at <c>pypi/simple/</c>, the files at <c>pypi/files/</c> and
/// the legacy upload API at <c>pypi/legacy/</c>.
/// See: https://packaging.python.org/en/latest/specifications/simple-repository-api/
/// </summary>
[FeedType(FeedType.PyPI)]
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class PyPIPackageController : Controller
{
    private const string JsonContentType = "application/vnd.pypi.simple.v1+json";
    private const string HtmlContentType = "application/vnd.pypi.simple.v1+html";
    private const string ApiVersion = "1.1";

    private const long BytesPerMiB = 1024L * 1024;

    // What an upload may have besides the file: the metadata fields, the description included.
    private const long MaxFormFieldBytes = 16 * BytesPerMiB;

    private readonly IPyPIPackageService _packages;
    private readonly IFeedContext _feedContext;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IAuthenticationService _authentication;
    private readonly IPermissionService _permissionService;
    private readonly IOptionsSnapshot<PaGettoOptions> _options;
    private readonly WebAuditLog _audit;

    public PyPIPackageController(
        IPyPIPackageService packages,
        IFeedContext feedContext,
        IFeedSettingsResolver feedSettings,
        IAuthenticationService authentication,
        IPermissionService permissionService,
        IOptionsSnapshot<PaGettoOptions> options,
        WebAuditLog audit)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <summary>
    /// The forms of the simple API, in the order they are preferred when a client accepts several
    /// equally.
    /// </summary>
    private enum SimpleFormat
    {
        Json,
        Html,
        TextHtml,
    }

    // GET pypi/simple/
    // The projects that have files in the feed.
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var names = await _packages.ListProjectsAsync(_feedContext.CurrentFeed, cancellationToken);
        var format = GetFormat();

        if (format == SimpleFormat.Json)
        {
            return SimpleJson(writer =>
            {
                writer.WriteStartArray("projects");
                foreach (var name in names)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", name);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            });
        }

        var html = SimpleHtmlStart("Simple index");
        foreach (var name in names)
        {
            html.Append("    <a href=\"").Append(Attribute(Url.PyPIProject(name))).Append("\">")
                .Append(HtmlEncoder.Default.Encode(name)).Append("</a><br/>\n");
        }

        return SimpleHtml(html, format);
    }

    // GET pypi/simple/{name}/
    // The files of a project.
    public async Task<IActionResult> Project(string name, CancellationToken cancellationToken)
    {
        if (!PyPIPackageName.IsValid(name))
        {
            return NotFound();
        }

        // Clients normalize the name themselves, but the page of a name is at its normalized name.
        var normalized = PyPIPackageName.Normalize(name);
        if (name != normalized)
        {
            return RedirectPermanent(Url.PyPIProject(normalized));
        }

        var project = await _packages.GetProjectOrNullAsync(_feedContext.CurrentFeed, normalized, cancellationToken);
        if (project == null)
        {
            return NotFound();
        }

        var format = GetFormat();
        if (format == SimpleFormat.Json)
        {
            return SimpleJson(writer =>
            {
                writer.WriteString("name", project.Name);

                writer.WriteStartArray("versions");
                foreach (var version in project.Versions)
                {
                    writer.WriteStringValue(version);
                }

                writer.WriteEndArray();

                writer.WriteStartArray("files");
                foreach (var file in project.Files)
                {
                    WriteFile(writer, project.Name, file);
                }

                writer.WriteEndArray();
            });
        }

        var title = $"Links for {project.Name}";
        var html = SimpleHtmlStart(title);
        html.Append("    <h1>").Append(HtmlEncoder.Default.Encode(title)).Append("</h1>\n");
        foreach (var file in project.Files)
        {
            var url = Url.PyPIFile(project.Name, file.Version, file.FileName);
            if (file.Sha256 != null)
            {
                url += "#sha256=" + file.Sha256;
            }

            html.Append("    <a href=\"").Append(Attribute(url)).Append('"');
            if (file.RequiresPython != null)
            {
                html.Append(" data-requires-python=\"").Append(Attribute(file.RequiresPython)).Append('"');
            }

            if (file.Yanked)
            {
                html.Append(" data-yanked=\"").Append(Attribute(file.YankedReason ?? string.Empty)).Append('"');
            }

            if (file.CoreMetadataSha256 != null)
            {
                // The second attribute is the name that pip before 23.3 reads.
                html.Append(" data-core-metadata=\"sha256=").Append(file.CoreMetadataSha256).Append('"');
                html.Append(" data-dist-info-metadata=\"sha256=").Append(file.CoreMetadataSha256).Append('"');
            }

            html.Append('>').Append(HtmlEncoder.Default.Encode(file.FileName)).Append("</a><br/>\n");
        }

        return SimpleHtml(html, format);
    }

    // GET, HEAD pypi/files/{name}/{version}/{fileName}
    // A wheel or sdist, or the core metadata of a wheel (its name with ".metadata").
    public async Task<IActionResult> Download(string name, string version, string fileName, CancellationToken cancellationToken)
    {
        if (!PyPIPackageName.IsValid(name))
        {
            return NotFound();
        }

        var download = await _packages.DownloadOrNullAsync(
            _feedContext.CurrentFeed,
            PyPIPackageName.Normalize(name),
            version,
            fileName,
            countDownload: !HttpMethods.IsHead(Request.Method),
            cancellationToken);

        if (download == null)
        {
            return NotFound();
        }

        var file = download.File;

        return File(
            download.Stream,
            "application/octet-stream",
            new DateTimeOffset(DateTime.SpecifyKind(file.CreatedAtUtc, DateTimeKind.Utc)),
            new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{file.Sha256}\""));
    }

    // POST pypi/legacy/
    // An upload of one file, as a multipart form with the file as "content" and its metadata as
    // fields. twine and uv publish send one request per file.
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;

        var denied = await CheckCanPublishAsync(cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        // The server-wide request limit applies before the feed is known; a feed can only lower it.
        var maxBytes = _feedSettings.GetMaxPackageSizeMiB(feed) * BytesPerMiB;
        if (Request.ContentLength > maxBytes + MaxFormFieldBytes)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_too_large", null, null);
            return TooLarge(maxBytes);
        }

        var sizeLimit = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = maxBytes + MaxFormFieldBytes;
        }

        if (!Request.HasFormContentType)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", null, null);
            return PyPIError(StatusCodes.Status400BadRequest, "The upload must be a multipart form.");
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

        var action = form[":action"].ToString();
        if (!string.Equals(action, "file_upload", StringComparison.Ordinal))
        {
            await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", null, null);
            return PyPIError(StatusCodes.Status400BadRequest, $"The action '{action}' is not supported; only file_upload is.");
        }

        var content = form.Files.GetFile("content");
        if (content == null)
        {
            await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", null, null);
            return PyPIError(StatusCodes.Status400BadRequest, "The upload has no file in its 'content' field.");
        }

        PyPIUploadResult result;
        await using (var stream = content.OpenReadStream())
        {
            result = await _packages.UploadAsync(
                feed,
                new PyPIUpload
                {
                    Name = form["name"].ToString(),
                    Version = form["version"].ToString(),
                    FileName = Path.GetFileName(content.FileName),
                    Content = stream,
                    Sha256Digest = form["sha256_digest"].ToString(),
                    Md5Digest = form["md5_digest"].ToString(),
                },
                maxBytes,
                cancellationToken);
        }

        switch (result.Status)
        {
            case PyPIUploadStatus.Success:
                await AuditAsync(LogLevel.Information, "package_upload_succeeded", result.Name, result.Version);
                return Content("OK", "text/plain");

            case PyPIUploadStatus.Conflict:
                await AuditAsync(LogLevel.Warning, "package_upload_already_exists", result.Name, result.Version);
                return PyPIError(StatusCodes.Status409Conflict, result.Message);

            case PyPIUploadStatus.TooLarge:
                await AuditAsync(LogLevel.Warning, "package_upload_too_large", result.Name, result.Version);
                return PyPIError(StatusCodes.Status413PayloadTooLarge, result.Message);

            default:
                await AuditAsync(LogLevel.Warning, "package_upload_invalid_package", result.Name, result.Version);
                return PyPIError(StatusCodes.Status400BadRequest, result.Message);
        }
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
            // Static auth mode: the configured API key, sent as the password.
            authorized = await _authentication.AuthenticateAsync(GetApiKey(), cancellationToken);
        }
        else
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            authorized = Guid.TryParse(userIdClaim, out var userId)
                && await _permissionService.CanPushAsync(userId, feed.Id, cancellationToken);
        }

        if (!authorized)
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Challenge(AuthenticationConstants.NugetBasicAuthenticationScheme);
            }

            await AuditAsync(LogLevel.Warning, "package_upload_unauthorized", null, null);
            return PyPIError(StatusCodes.Status403Forbidden, "You do not have permission to publish packages to this feed.");
        }

        if (_feedSettings.GetIsReadOnlyMode(feed))
        {
            await AuditAsync(LogLevel.Warning, "package_upload_read_only", null, null);
            return PyPIError(StatusCodes.Status403Forbidden, "This feed is read-only.");
        }

        return null;
    }

    /// <summary>
    /// The form the client prefers, from its <c>Accept</c> header (PEP 691). Without one, or when it
    /// accepts none of them, it is HTML.
    /// </summary>
    private SimpleFormat GetFormat()
    {
        var best = SimpleFormat.TextHtml;
        var bestQuality = -1.0;

        foreach (var value in Request.GetTypedHeaders().Accept)
        {
            var quality = value.Quality ?? 1.0;
            SimpleFormat? format = value.MediaType.Value?.ToLowerInvariant() switch
            {
                "application/vnd.pypi.simple.v1+json" or "application/vnd.pypi.simple.latest+json" => SimpleFormat.Json,
                "application/vnd.pypi.simple.v1+html" or "application/vnd.pypi.simple.latest+html" => SimpleFormat.Html,
                "text/html" or "text/*" or "*/*" => SimpleFormat.TextHtml,
                _ => null,
            };

            if (format is { } accepted && quality > 0 && (quality > bestQuality || (quality == bestQuality && accepted < best)))
            {
                best = accepted;
                bestQuality = quality;
            }
        }

        return best;
    }

    private void WriteFile(Utf8JsonWriter writer, string name, PyPIProjectFile file)
    {
        writer.WriteStartObject();
        writer.WriteString("filename", file.FileName);
        writer.WriteString("url", Url.PyPIFile(name, file.Version, file.FileName));

        writer.WriteStartObject("hashes");
        if (file.Sha256 != null)
        {
            writer.WriteString("sha256", file.Sha256);
        }

        writer.WriteEndObject();

        if (file.RequiresPython != null)
        {
            writer.WriteString("requires-python", file.RequiresPython);
        }

        if (file.Yanked && file.YankedReason != null)
        {
            writer.WriteString("yanked", file.YankedReason);
        }
        else
        {
            writer.WriteBoolean("yanked", file.Yanked);
        }

        // The second key is the name that pip before 23.3 reads.
        foreach (var key in new[] { "core-metadata", "dist-info-metadata" })
        {
            if (file.CoreMetadataSha256 != null)
            {
                writer.WriteStartObject(key);
                writer.WriteString("sha256", file.CoreMetadataSha256);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteBoolean(key, false);
            }
        }

        if (file.Size is { } size)
        {
            writer.WriteNumber("size", size);
        }

        if (file.UploadTime is { } uploadTime)
        {
            writer.WriteString("upload-time", uploadTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture));
        }

        writer.WriteEndObject();
    }

    private FileContentResult SimpleJson(Action<Utf8JsonWriter> writeBody)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("meta");
            writer.WriteString("api-version", ApiVersion);
            writer.WriteEndObject();
            writeBody(writer);
            writer.WriteEndObject();
        }

        // The same URL answers in several forms.
        Response.Headers.Vary = "Accept";

        return File(buffer.ToArray(), JsonContentType);
    }

    private static StringBuilder SimpleHtmlStart(string title)
    {
        return new StringBuilder()
            .Append("<!DOCTYPE html>\n<html>\n  <head>\n")
            .Append("    <meta name=\"pypi:repository-version\" content=\"").Append(ApiVersion).Append("\">\n")
            .Append("    <title>").Append(HtmlEncoder.Default.Encode(title)).Append("</title>\n")
            .Append("  </head>\n  <body>\n");
    }

    private ContentResult SimpleHtml(StringBuilder html, SimpleFormat format)
    {
        html.Append("  </body>\n</html>\n");

        Response.Headers.Vary = "Accept";

        return Content(html.ToString(), format == SimpleFormat.Html ? HtmlContentType : "text/html; charset=utf-8");
    }

    private static string Attribute(string value)
    {
        return HtmlEncoder.Default.Encode(value);
    }

    /// <summary>
    /// The API key in Legacy mode: the password of Basic authentication, which is what twine and
    /// uv send, or the NuGet API key header.
    /// </summary>
    private string GetApiKey()
    {
        var apiKey = Request.Headers[HttpRequestExtensions.ApiKeyHeader].ToString();
        if (!string.IsNullOrEmpty(apiKey))
        {
            return apiKey;
        }

        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header) || header.Parameter == null)
        {
            return null;
        }

        if (string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return header.Parameter;
        }

        try
        {
            var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter)).Split(':', 2);
            return credentials.Length == 2 ? credentials[1] : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private ContentResult TooLarge(long maxBytes)
    {
        return PyPIError(StatusCodes.Status413PayloadTooLarge, $"The file is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.");
    }

    /// <summary>
    /// An error as plain text, with the message as the reason phrase too: twine shows the reason
    /// phrase of a failed upload, like PyPI's.
    /// </summary>
    private ContentResult PyPIError(int statusCode, string message)
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
            actor = string.IsNullOrEmpty(GetApiKey()) ? "anonymous" : "api-key";
        }

        await _audit.PackageAsync(HttpContext, level, eventName, _feedContext.CurrentFeed.Slug, packageName, packageVersion, actor);
    }
}
