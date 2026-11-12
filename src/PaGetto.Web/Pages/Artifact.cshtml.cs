using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Markdig;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Maven;
using PaGetto.Core.Npm;
using PaGetto.Core.Pub;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;

namespace PaGetto.Web.Pages;

/// <summary>
/// The page of a package of a feed that is not a NuGet feed. It shows the versions stored in the
/// feed; versions that only a mirror has appear once they have been downloaded.
/// </summary>
public class ArtifactModel : PageModel
{
    // Readmes come from package authors and from mirrors, so their raw HTML is not rendered.
    private static readonly MarkdownPipeline _markdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly IFeedContext _feedContext;
    private readonly IPermissionService _permissions;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly WebAuditLog _audit;
    private readonly IOptionsSnapshot<NugetAuthenticationOptions> _authOptions;

    public ArtifactModel(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        IFeedContext feedContext,
        IPermissionService permissions,
        IFeedSettingsResolver feedSettings,
        WebAuditLog audit,
        IOptionsSnapshot<NugetAuthenticationOptions> authOptions)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _authOptions = authOptions ?? throw new ArgumentNullException(nameof(authOptions));
    }

    public bool Found { get; private set; }

    /// <summary>
    /// The package name from the URL. It is only shown as encoded text.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// The requested version when the package has no such version; the page then says so instead
    /// of showing another version.
    /// </summary>
    public string VersionNotFound { get; private set; }

    /// <summary>
    /// The version the page shows.
    /// </summary>
    public ArtifactVersion Version { get; private set; }

    /// <summary>
    /// Whether <see cref="Version"/> is the version shown by default.
    /// </summary>
    public bool IsLatest { get; private set; }

    /// <summary>
    /// All versions stored in the feed, newest first.
    /// </summary>
    public IReadOnlyList<ArtifactVersion> Versions { get; private set; }

    public long TotalDownloads { get; private set; }
    public DateTime LastUpdated { get; private set; }
    public HtmlString Readme { get; private set; }

    /// <summary>
    /// The manifest of a Dart package. Null on feeds of other types.
    /// </summary>
    public PubspecSummary Pubspec { get; private set; }

    /// <summary>
    /// The manifest of an npm package. Null on feeds of other types.
    /// </summary>
    public NpmManifestSummary NpmManifest { get; private set; }

    /// <summary>
    /// The manifest of a Maven version, read from its POM. Null on feeds of other types.
    /// </summary>
    public MavenPomSummary MavenPom { get; private set; }

    /// <summary>
    /// The files of the version, for ecosystems whose versions have several (Maven). Empty otherwise.
    /// </summary>
    public IReadOnlyList<ArtifactFile> Files { get; private set; } = [];

    /// <summary>
    /// Whether the Manage section and the Relist links are shown: the user may delete from the feed,
    /// and the feed is not read-only.
    /// </summary>
    public bool CanManage { get; private set; }

    public async Task<IActionResult> OnGetAsync(string id, string version, CancellationToken cancellationToken)
    {
        var authMode = _authOptions.Value.Mode;
        if (FeedAccessGuard.RequiresSignIn(HttpContext, authMode)) return Page();

        var denied = await FeedAccessGuard.CheckReadAccessAsync(
            HttpContext, _feedContext, _permissions, authMode, cancellationToken);
        if (denied != null) return denied;

        var feed = _feedContext.CurrentFeed;
        if (feed.Type == FeedType.NuGet) return NotFound();

        CanManage = !_feedSettings.GetIsReadOnlyMode(feed)
            && await FeedAccessGuard.CanDeleteFromCurrentFeedAsync(HttpContext, _feedContext, _permissions, authMode, cancellationToken);

        Name = id = DecodeName(id);

        // Names are stored lowercased.
        var versions = await _artifacts.FindVersionsAsync(feed.Id, id.ToLowerInvariant(), cancellationToken);
        if (versions.Count == 0)
        {
            return Page();
        }

        Name = versions[0].Artifact.Name;

        var tags = await _artifacts.FindTagsAsync(feed.Id, Name, cancellationToken);
        var latest = ArtifactVersionOrder.GetLatest(feed.Type, versions, tags);
        if (string.IsNullOrEmpty(version))
        {
            Version = latest;
        }
        else
        {
            Version = versions.FirstOrDefault(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));
            if (Version == null)
            {
                VersionNotFound = version;
                return Page();
            }
        }

        Found = true;
        IsLatest = Version == latest;
        Versions = ArtifactVersionOrder.SortDescending(feed.Type, versions);
        TotalDownloads = versions.Sum(v => v.Downloads);
        LastUpdated = versions.Max(v => v.Published);

        if (feed.Type == FeedType.Pub)
        {
            Pubspec = PubspecSummary.Parse(Version.MetadataJson);
        }
        else if (feed.Type == FeedType.Npm)
        {
            NpmManifest = NpmManifestSummary.Parse(Version.MetadataJson);
        }
        else if (feed.Type == FeedType.Maven)
        {
            MavenPom = MavenPomSummary.Parse(Version.MetadataJson);
            Files = await _artifacts.FindFilesAsync(feed.Id, Name, Version.Version, cancellationToken);

            // The coordinates as published; the stored name is lowercased.
            Name = MavenPom.Coordinates ?? Name;
        }

        if (Version.HasReadme)
        {
            await using var readmeStream = await _storage.GetReadmeStreamAsync(feed.Slug, Name, Version.Version, cancellationToken);
            using var reader = new StreamReader(readmeStream);

            Readme = new HtmlString(Markdown.ToHtml(await reader.ReadToEndAsync(cancellationToken), _markdownPipeline));
        }

        return Page();
    }

    public async Task<IActionResult> OnPostUnlistAsync(string id, string version, CancellationToken cancellationToken)
    {
        return await ManageVersionAsync(
            "unlist", id, version,
            (feed, name) => _artifacts.SetListedAsync(feed.Id, name, version, listed: false, cancellationToken),
            RedirectToPage(new { id = DecodeName(id), version }),
            cancellationToken);
    }

    public async Task<IActionResult> OnPostRelistAsync(string id, string version, CancellationToken cancellationToken)
    {
        return await ManageVersionAsync(
            "relist", id, version,
            (feed, name) => _artifacts.SetListedAsync(feed.Id, name, version, listed: true, cancellationToken),
            RedirectToPage(new { id = DecodeName(id), version }),
            cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id, string version, CancellationToken cancellationToken)
    {
        return await ManageVersionAsync(
            "delete", id, version,
            async (feed, name) =>
            {
                // The files are listed before the version and its file rows are gone.
                var files = await _artifacts.FindFilesAsync(feed.Id, name, version, cancellationToken);

                if (!await _artifacts.HardDeleteVersionAsync(feed.Id, name, version, cancellationToken))
                {
                    return false;
                }

                await _storage.DeleteAsync(feed.Slug, name, version, cancellationToken);
                foreach (var file in files)
                {
                    await _storage.DeleteFileAsync(feed.Slug, name, version, file.FileName, cancellationToken);
                }

                return true;
            },
            // version = null drops the ambient route value; without it the redirect would point at
            // the version that was just deleted.
            RedirectToPage(new { id = DecodeName(id), version = (string)null }),
            cancellationToken);
    }

    /// <summary>
    /// Runs an unlist, relist or delete from the Manage section and writes its audit line
    /// (<c>package_{action}_{succeeded,unauthorized,read_only,not_found}</c>), like the NuGet package page.
    /// </summary>
    private async Task<IActionResult> ManageVersionAsync(
        string action,
        string id,
        string version,
        Func<Feed, string, Task<bool>> operation,
        IActionResult success,
        CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed;
        var authMode = _authOptions.Value.Mode;

        if (feed.Type == FeedType.NuGet || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(version))
        {
            return NotFound();
        }

        id = DecodeName(id);

        if (!await FeedAccessGuard.CanDeleteFromCurrentFeedAsync(HttpContext, _feedContext, _permissions, authMode, cancellationToken))
        {
            await _audit.PackageAsync(HttpContext, LogLevel.Warning, $"package_{action}_unauthorized", feed.Slug, id, version);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (_feedSettings.GetIsReadOnlyMode(feed))
        {
            await _audit.PackageAsync(HttpContext, LogLevel.Warning, $"package_{action}_read_only", feed.Slug, id, version);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        // Names are stored lowercased.
        var found = await operation(feed, id.ToLowerInvariant());
        await _audit.PackageAsync(
            HttpContext,
            found ? LogLevel.Information : LogLevel.Warning,
            found ? $"package_{action}_succeeded" : $"package_{action}_not_found",
            feed.Slug, id, version);

        return found ? success : NotFound();
    }

    /// <summary>
    /// A scoped npm name has a slash, which is in the URL as <c>%2F</c>. ASP.NET Core leaves that
    /// encoded in route values.
    /// </summary>
    private static string DecodeName(string id)
    {
        return id.Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
    }
}
