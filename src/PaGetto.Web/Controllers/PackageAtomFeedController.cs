using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using PaGetto.Core;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Web.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace PaGetto.Web.Controllers;

/// <summary>
/// An Atom feed of a package's listed versions, newest first, so people can subscribe to new
/// releases. Feed readers can't use the web UI cookie, so in Local, Entra and Hybrid mode they
/// authenticate with Basic auth (a username and a personal access token).
/// </summary>
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme)]
public class PackageAtomFeedController : Controller
{
    public const int MaxEntries = 20;

    private const string AtomNamespace = "http://www.w3.org/2005/Atom";

    private readonly IPackageService _packages;
    private readonly IFeedContext _feedContext;
    private readonly IPermissionService _permissions;
    private readonly IOptionsSnapshot<NugetAuthenticationOptions> _authOptions;

    public PackageAtomFeedController(
        IPackageService packages,
        IFeedContext feedContext,
        IPermissionService permissions,
        IOptionsSnapshot<NugetAuthenticationOptions> authOptions)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _authOptions = authOptions ?? throw new ArgumentNullException(nameof(authOptions));
    }

    // An attribute route rather than one in PaGettoEndpointBuilder: conventional routes are ordered
    // after Razor Pages, so the package page (/packages/{id}/{version?}) would take this URL.
    [HttpGet("packages/{id}/atom.xml", Name = Routes.PackageAtomFeedRouteName)]
    public async Task<IActionResult> GetAsync(string id, CancellationToken cancellationToken)
    {
        // The same check as the package page: a user without pull permission gets a 404.
        var denied = await FeedAccessGuard.CheckReadAccessAsync(
            HttpContext, _feedContext, _permissions, _authOptions.Value.Mode, cancellationToken);
        if (denied != null) return denied;

        // Includes versions that are only available from a mirror, like the package page.
        var packages = await _packages.FindPackagesAsync(_feedContext.CurrentFeed.Id, id, cancellationToken);
        var entries = packages
            .Where(p => p.Listed)
            .OrderByDescending(p => p.Published)
            .ThenByDescending(p => p.Version)
            .Take(MaxEntries)
            .ToList();

        if (entries.Count == 0) return NotFound();

        var packageId = entries[0].Id;
        var selfUrl = Url.RouteUrl(Routes.PackageAtomFeedRouteName, new { id = packageId }, Request.Scheme);
        var packageUrl = Url.Page("/Package", null, new { id = packageId, version = (string)null }, Request.Scheme);

        using var buffer = new MemoryStream();
        var settings = new XmlWriterSettings { Async = true, Encoding = new UTF8Encoding(false), Indent = true };
        await using (var writer = XmlWriter.Create(buffer, settings))
        {
            await writer.WriteStartDocumentAsync();
            await writer.WriteStartElementAsync(null, "feed", AtomNamespace);

            await WriteElementAsync(writer, "id", selfUrl);
            await WriteElementAsync(writer, "title", $"{packageId} versions");
            await WriteElementAsync(writer, "updated", FormatDate(entries[0].Published));
            await WriteLinkAsync(writer, "self", "application/atom+xml", selfUrl);
            await WriteLinkAsync(writer, "alternate", "text/html", packageUrl);

            // Atom requires an author on the feed or on every entry.
            await writer.WriteStartElementAsync(null, "author", AtomNamespace);
            var authors = entries[0].Authors ?? [];
            await WriteElementAsync(writer, "name", authors.Length > 0 ? string.Join(", ", authors) : packageId);
            await writer.WriteEndElementAsync();

            foreach (var package in entries)
            {
                var versionUrl = Url.Page("/Package", null, new { id = packageId, version = package.NormalizedVersionString }, Request.Scheme);

                await writer.WriteStartElementAsync(null, "entry", AtomNamespace);
                await WriteElementAsync(writer, "id", versionUrl);
                await WriteElementAsync(writer, "title", $"{packageId} {package.NormalizedVersionString}");
                await WriteElementAsync(writer, "updated", FormatDate(package.Published));
                await WriteLinkAsync(writer, "alternate", "text/html", versionUrl);

                if (!string.IsNullOrWhiteSpace(package.Description))
                {
                    await WriteElementAsync(writer, "summary", package.Description);
                }

                if (!string.IsNullOrWhiteSpace(package.ReleaseNotes))
                {
                    await writer.WriteStartElementAsync(null, "content", AtomNamespace);
                    await writer.WriteAttributeStringAsync(null, "type", null, "text");
                    await writer.WriteStringAsync(package.ReleaseNotes);
                    await writer.WriteEndElementAsync();
                }

                await writer.WriteEndElementAsync();
            }

            await writer.WriteEndElementAsync();
            await writer.WriteEndDocumentAsync();
        }

        return File(buffer.ToArray(), "application/atom+xml; charset=utf-8");
    }

    private static Task WriteElementAsync(XmlWriter writer, string name, string value)
    {
        return writer.WriteElementStringAsync(null, name, AtomNamespace, value);
    }

    private static async Task WriteLinkAsync(XmlWriter writer, string rel, string type, string href)
    {
        await writer.WriteStartElementAsync(null, "link", AtomNamespace);
        await writer.WriteAttributeStringAsync(null, "rel", null, rel);
        await writer.WriteAttributeStringAsync(null, "type", null, type);
        await writer.WriteAttributeStringAsync(null, "href", null, href);
        await writer.WriteEndElementAsync();
    }

    /// <summary>
    /// RFC 3339 in UTC. Dates are stored in UTC; the database may return them without a kind.
    /// </summary>
    private static string FormatDate(DateTime published)
    {
        return DateTime.SpecifyKind(published, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
