using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Content;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Indexing;
using PaGetto.Core.Search;
using PaGetto.Web.Audit;
using PaGetto.Web.Authentication;
using PaGetto.Web.Extensions;
using PaGetto.Web.Helper;
using Markdig;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace PaGetto.Web.Pages;

public class PackageModel : PageModel
{
    private static readonly MarkdownPipeline _markdownPipeline;

    private readonly IPackageService _packages;
    private readonly IPackageContentService _content;
    private readonly ISearchService _search;
    private readonly IUrlGenerator _url;
    private readonly IFeedContext _feedContext;
    private readonly IPermissionService _permissions;
    private readonly IPackageDeletionService _deletionService;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly WebAuditLog _audit;
    private readonly IOptionsSnapshot<NugetAuthenticationOptions> _authOptions;
    private readonly SystemTime _time;

    static PackageModel()
    {
        _markdownPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();
    }

    public PackageModel(
        IPackageService packages,
        IPackageContentService content,
        ISearchService search,
        IUrlGenerator url,
        IFeedContext feedContext,
        IPermissionService permissions,
        IPackageDeletionService deletionService,
        IFeedSettingsResolver feedSettings,
        WebAuditLog audit,
        IOptionsSnapshot<NugetAuthenticationOptions> authOptions,
        SystemTime time)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _deletionService = deletionService ?? throw new ArgumentNullException(nameof(deletionService));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _authOptions = authOptions ?? throw new ArgumentNullException(nameof(authOptions));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public bool Found { get; private set; }

    /// <summary>
    /// Whether the current user may unlist/delete packages in this feed. Gates the
    /// Unlist and Delete buttons on the page. False in Config mode and for anonymous users.
    /// </summary>
    public bool CanDelete { get; private set; }

    /// <summary>
    /// Whether the feed is in read-only mode. Unlist, relist and delete are refused then.
    /// </summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>
    /// Whether the Manage section and the Relist links are shown.
    /// </summary>
    public bool CanManage => CanDelete && !IsReadOnly;

    /// <summary>
    /// Whether the shown version is stored in this feed. Versions that are only available from a
    /// mirror can't be unlisted or deleted here.
    /// </summary>
    public bool IsStoredLocally { get; private set; }

    /// <summary>
    /// The requested version when it doesn't exist; the page then says so instead of showing
    /// another version.
    /// </summary>
    public string VersionNotFound { get; private set; }

    /// <summary>
    /// The package id from the URL when the feed has no such package. It is kept apart from
    /// <see cref="Package"/>, which only ever holds a package from the database, so the id
    /// typed into the URL is only shown as encoded text.
    /// </summary>
    public string PackageIdNotFound { get; private set; }

    public Package Package { get; private set; }

    public bool IsDotnetTemplate { get; private set; }
    public bool IsDotnetTool { get; private set; }
    public DateTime LastUpdated { get; private set; }
    public long TotalDownloads { get; private set; }

    /// <summary>
    /// Total downloads divided by the days since the earliest version stored in this feed was
    /// published (at least one day). Null when no version is stored in this feed.
    /// </summary>
    public long? DailyDownloadAverage { get; private set; }

    /// <summary>
    /// Whether any shown version is a prerelease; the versions table then offers a prerelease filter.
    /// </summary>
    public bool HasPrereleaseVersions => Versions?.Any(v => v.IsPrerelease) == true;

    public IReadOnlyList<PackageDependent> UsedBy { get; set; }
    public IReadOnlyList<DependencyGroupModel> DependencyGroups { get; private set; }
    public IReadOnlyList<VersionModel> Versions { get; private set; }

    /// <summary>
    /// The lowest target framework per family (e.g. ".NET 6.0", ".NET Standard 2.0"), shown as badges
    /// under the title.
    /// </summary>
    public IReadOnlyList<string> FrameworkBadges { get; private set; }

    /// <summary>
    /// Every target framework of the package, as display names, sorted by family and version.
    /// </summary>
    public IReadOnlyList<string> Frameworks { get; private set; }

    public HtmlString Readme { get; private set; }

    public HtmlString ParsedReleaseNotes { get; private set; }

    public string IconUrl { get; private set; }
    public string LicenseUrl { get; private set; }

    /// <summary>
    /// "{expression} license" for packages with a license expression, otherwise "License".
    /// </summary>
    public string LicenseText { get; private set; }

    /// <summary>
    /// The .nupkg size, e.g. "2.43 MB", or null when it isn't known.
    /// </summary>
    public string PackageSize { get; private set; }
    public string PackageDownloadUrl { get; private set; }

    public async Task<IActionResult> OnGetAsync(string id, string version, CancellationToken cancellationToken)
    {
        if (FeedAccessGuard.RequiresSignIn(HttpContext, _authOptions.Value.Mode)) return Page();

        var denied = await FeedAccessGuard.CheckReadAccessAsync(
            HttpContext, _feedContext, _permissions, _authOptions.Value.Mode, cancellationToken);
        if (denied != null) return denied;

        CanDelete = await FeedAccessGuard.CanDeleteFromCurrentFeedAsync(
            HttpContext, _feedContext, _permissions, _authOptions.Value.Mode, cancellationToken);
        IsReadOnly = _feedSettings.GetIsReadOnlyMode(_feedContext.CurrentFeed);

        var packages = await _packages.FindPackagesAsync(_feedContext.CurrentFeed.Id, id, cancellationToken);
        var listedPackages = packages.Where(p => p.Listed).ToList();

        if (!string.IsNullOrEmpty(version))
        {
            // A requested version that doesn't exist is reported, not replaced by the latest one.
            if (NuGetVersion.TryParse(version, out var requestedVersion))
            {
                Package = packages.SingleOrDefault(p => p.Version == requestedVersion);
            }

            if (Package == null && packages.Count > 0)
            {
                Package = new Package { Id = packages[0].Id };
                VersionNotFound = version;
                Found = false;
                return Page();
            }
        }

        // Otherwise display the latest version.
        Package ??= listedPackages.OrderByDescending(p => p.Version).FirstOrDefault();

        if (Package == null)
        {
            PackageIdNotFound = id;
            Found = false;
            return Page();
        }

        IsStoredLocally = IsLocal(Package);

        var packageVersion = Package.Version;

        Found = true;
        IsDotnetTemplate = Package.PackageTypes.Any(t => t.Name.Equals("Template", StringComparison.OrdinalIgnoreCase));
        IsDotnetTool = Package.PackageTypes.Any(t => t.Name.Equals("DotnetTool", StringComparison.OrdinalIgnoreCase));
        LastUpdated = packages.Max(p => p.Published);
        TotalDownloads = packages.Sum(p => p.Downloads);
        DailyDownloadAverage = GetDailyDownloadAverage(packages, TotalDownloads, _time.UtcNow);

        var dependents = await _search.FindDependentsAsync(_feedContext.CurrentFeed.Id, Package.Id, cancellationToken);

        UsedBy = dependents.Data;
        DependencyGroups = ToDependencyGroups(Package);

        // Mirrored packages may have no target frameworks.
        var monikers = (Package.TargetFrameworks ?? [])
            .Select(f => f.Moniker)
            .Where(m => !string.IsNullOrEmpty(m) && !m.Equals("any", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        FrameworkBadges = TargetFrameworkNames.GetLowestPerFamily(monikers);
        Frameworks = TargetFrameworkNames.Sort(monikers).Select(TargetFrameworkNames.GetDisplayName).ToList();

        // Managers (CanDelete) also see unlisted versions of this feed so they can relist them;
        // the versions table strikes those through. Unlisted versions that only exist on a mirror
        // can't be relisted, so they stay hidden. Everyone else only sees listed versions.
        var versionsToShow = CanDelete
            ? packages.Where(p => p.Listed || IsLocal(p)).ToList()
            : listedPackages;
        Versions = ToVersions(versionsToShow, packageVersion);

        if (Package.HasReadme)
        {
            Readme = await GetReadmeHtmlStringOrNullAsync(Package.Id, packageVersion, cancellationToken);
        }

        ParsedReleaseNotes = ParseReleaseNotes();

        IconUrl = Package.HasEmbeddedIcon
            ? _url.GetPackageIconDownloadUrl(Package.Id, packageVersion)
            : Package.IconUrlString;
        if (string.IsNullOrEmpty(Package.LicenseExpression))
        {
            LicenseUrl = Package.LicenseUrlString;
            LicenseText = "License";
        }
        else
        {
            LicenseUrl = "https://licenses.nuget.org/" + Uri.EscapeDataString(Package.LicenseExpression);
            LicenseText = Package.LicenseExpression + " license";
        }

        PackageSize = Package.Size?.ToFileSize();
        PackageDownloadUrl = _url.GetPackageDownloadUrl(Package.Id, packageVersion);

        return Page();
    }

    public async Task<IActionResult> OnPostUnlistAsync(string id, string version, CancellationToken cancellationToken)
    {
        return await ManageVersionAsync(
            "unlist", id, version,
            v => _deletionService.TryUnlistPackageAsync(_feedContext.CurrentFeed.Id, id, v, cancellationToken),
            RedirectToPage(new { id, version }),
            cancellationToken);
    }

    public async Task<IActionResult> OnPostRelistAsync(string id, string version, CancellationToken cancellationToken)
    {
        return await ManageVersionAsync(
            "relist", id, version,
            v => _deletionService.TryRelistPackageAsync(_feedContext.CurrentFeed.Id, id, v, cancellationToken),
            RedirectToPage(new { id, version }),
            cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id, string version, CancellationToken cancellationToken)
    {
        // The version is gone afterwards; land on the package's default view (latest remaining or not-found).
        return await ManageVersionAsync(
            "delete", id, version,
            v => _deletionService.TryHardDeletePackageAsync(_feedContext.CurrentFeed.Id, _feedContext.CurrentFeed.Slug, id, v, cancellationToken),
            // version = null drops the ambient route value; without it the redirect would point at
            // the version that was just deleted.
            RedirectToPage(new { id, version = (string)null }),
            cancellationToken);
    }

    /// <summary>
    /// Runs an unlist, relist or delete from the Manage section and writes its audit line
    /// (<c>package_{action}_{succeeded,unauthorized,read_only,not_found}</c>).
    /// </summary>
    private async Task<IActionResult> ManageVersionAsync(
        string action,
        string id,
        string version,
        Func<NuGetVersion, Task<bool>> operation,
        IActionResult success,
        CancellationToken cancellationToken)
    {
        var feed = _feedContext.CurrentFeed.Slug;

        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            _audit.Package(HttpContext, LogLevel.Warning, $"package_{action}_not_found", feed, id, version);
            return NotFound();
        }

        if (_feedSettings.GetIsReadOnlyMode(_feedContext.CurrentFeed))
        {
            _audit.Package(HttpContext, LogLevel.Warning, $"package_{action}_read_only", feed, id, version);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!await CanDeleteCurrentFeedAsync(cancellationToken))
        {
            _audit.Package(HttpContext, LogLevel.Warning, $"package_{action}_unauthorized", feed, id, version);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var found = await operation(nugetVersion);
        _audit.Package(
            HttpContext,
            found ? LogLevel.Information : LogLevel.Warning,
            found ? $"package_{action}_succeeded" : $"package_{action}_not_found",
            feed, id, version);

        return success;
    }

    /// <summary>
    /// Packages read from the database have a key; packages that only exist on a mirror come
    /// from the upstream client and don't.
    /// </summary>
    private static bool IsLocal(Package package)
    {
        return package.Key != 0;
    }

    /// <summary>
    /// Downloads are only counted for versions stored in this feed, so the average starts at the
    /// earliest of those, not at an upstream publish date.
    /// </summary>
    private static long? GetDailyDownloadAverage(IReadOnlyList<Package> packages, long totalDownloads, DateTime utcNow)
    {
        var local = packages.Where(IsLocal).ToList();
        if (local.Count == 0) return null;

        var days = Math.Max(1, (long)(utcNow - local.Min(p => p.Published)).TotalDays);
        return totalDownloads / days;
    }

    private Task<bool> CanDeleteCurrentFeedAsync(CancellationToken cancellationToken)
        => FeedAccessGuard.CanDeleteFromCurrentFeedAsync(
            HttpContext, _feedContext, _permissions, _authOptions.Value.Mode, cancellationToken);

    private static List<DependencyGroupModel> ToDependencyGroups(Package package)
    {
        return package
            .Dependencies
            .GroupBy(d => d.TargetFramework)
            .Select(group =>
            {
                return new DependencyGroupModel
                {
                    Name = TargetFrameworkNames.GetDisplayName(group.Key),
                    Dependencies = group
                        .Where(d => d.Id != null)
                        .Select(d => new DependencyModel
                        {
                            PackageId = d.Id,
                            VersionSpec = (d.VersionRange != null)
                                ? VersionRange.Parse(d.VersionRange).PrettyPrint()
                                : string.Empty
                        })
                        .ToList()
                };
            })
            .ToList();
    }

    private static List<VersionModel> ToVersions(IReadOnlyList<Package> packages, NuGetVersion selectedVersion)
    {
        return packages
            .Select(p => new VersionModel
            {
                Version = p.Version,
                Downloads = p.Downloads,
                Selected = p.Version == selectedVersion,
                // Upstreams report 1900-01-01 for unlisted versions they have no date for.
                LastUpdated = p.Published.Year > 1900 ? p.Published : null,
                Listed = p.Listed,
                IsPrerelease = p.Version.IsPrerelease,
                IsLocal = IsLocal(p),
            })
            .OrderByDescending(m => m.Version)
            .ToList();
    }

    private async Task<HtmlString> GetReadmeHtmlStringOrNullAsync(
        string packageId,
        NuGetVersion packageVersion,
        CancellationToken cancellationToken)
    {
        await using var readmeStream = await _content.GetPackageReadmeStreamOrNullAsync(_feedContext.CurrentFeed.Id, _feedContext.CurrentFeed.Slug, packageId, packageVersion, cancellationToken);
        if (readmeStream == null) return null;

        using var reader = new StreamReader(readmeStream);
        var readme = await reader.ReadToEndAsync(cancellationToken);

        var readmeHtml = Markdown.ToHtml(readme, _markdownPipeline);
        return new HtmlString(readmeHtml);
    }

    private HtmlString ParseReleaseNotes()
    {
        if (string.IsNullOrWhiteSpace(Package.ReleaseNotes))
        {
            return HtmlString.Empty;
        }

        var releseNotesHtml = Markdown.ToHtml(Package.ReleaseNotes, _markdownPipeline);
        return new HtmlString(releseNotesHtml);
    }

    public class DependencyGroupModel
    {
        public string Name { get; set; }
        public IReadOnlyList<DependencyModel> Dependencies { get; set; }
    }

    // TODO: Convert this to records.
    public class DependencyModel
    {
        public string PackageId { get; set; }
        public string VersionSpec { get; set; }
    }

    // TODO: Convert this to records.
    public class VersionModel
    {
        public NuGetVersion Version { get; set; }
        public long Downloads { get; set; }
        public bool Selected { get; set; }
        public DateTime? LastUpdated { get; set; }
        public bool Listed { get; set; }
        public bool IsPrerelease { get; set; }
        public bool IsLocal { get; set; }
    }
}
