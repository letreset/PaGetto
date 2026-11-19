using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Vulnerabilities;

namespace PaGetto.Web.Pages.Admin;

/// <summary>
/// The known vulnerabilities of the stored versions across feeds, with filters and an export.
/// </summary>
[Authorize(AuthenticationSchemes = AuthenticationConstants.CookieScheme)]
public class VulnerabilitiesModel : PageModel
{
    public const int PageSize = 50;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IVulnerabilityService _vulnerabilities;
    private readonly IUserService _userService;
    private readonly IFeedService _feedService;
    private readonly IOptionsSnapshot<VulnerabilityScanningOptions> _options;

    public VulnerabilitiesModel(
        IVulnerabilityService vulnerabilities,
        IUserService userService,
        IFeedService feedService,
        IOptionsSnapshot<VulnerabilityScanningOptions> options)
    {
        _vulnerabilities = vulnerabilities ?? throw new ArgumentNullException(nameof(vulnerabilities));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _feedService = feedService ?? throw new ArgumentNullException(nameof(feedService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// The slug of the feed to show, or null for every feed.
    /// </summary>
    [FromQuery(Name = "feed")]
    public string Feed { get; set; }

    /// <summary>
    /// The lowest severity to show, or null for every severity.
    /// </summary>
    [FromQuery(Name = "severity")]
    public VulnerabilitySeverity? Severity { get; set; }

    /// <summary>
    /// Text the package name or the advisory id contains.
    /// </summary>
    [FromQuery(Name = "q")]
    public string Query { get; set; }

    [FromQuery(Name = "p")]
    public int PageIndex { get; set; } = 1;

    public bool ScanningEnabled => _options.Value.Enabled;

    public IReadOnlyList<PackageVulnerability> Items { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int TotalPages { get; private set; } = 1;
    public List<Feed> Feeds { get; private set; } = [];

    /// <summary>
    /// The feed slugs by id, to name each finding's feed.
    /// </summary>
    public Dictionary<Guid, Feed> FeedsById { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        PageIndex = Math.Max(1, PageIndex);
        await LoadFeedsAsync(cancellationToken);

        (Items, TotalCount) = await _vulnerabilities.SearchAsync(CreateFilter(), (PageIndex - 1) * PageSize, PageSize, cancellationToken);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

        return Page();
    }

    /// <summary>
    /// Every finding that matches the filters, as CSV or JSON (<c>?handler=Export&amp;format=json</c>).
    /// </summary>
    public async Task<IActionResult> OnGetExportAsync(string format, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        await LoadFeedsAsync(cancellationToken);
        var (items, _) = await _vulnerabilities.SearchAsync(CreateFilter(), 0, null, cancellationToken);
        var rows = items.Select(v => new
        {
            feed = FeedsById.TryGetValue(v.FeedId, out var feed) ? feed.Slug : null,
            package = v.PackageName,
            version = v.PackageVersion,
            advisory = v.AdvisoryId,
            aliases = v.Aliases,
            severity = v.Severity.ToString(),
            cvssScore = v.CvssScore,
            summary = v.Summary,
            affectedVersions = v.AffectedVersions,
            fixedVersion = v.FixedVersion,
            // SQLite gives back dates without their kind; they are stored in UTC.
            foundAtUtc = DateTime.SpecifyKind(v.FoundAtUtc, DateTimeKind.Utc),
        }).ToList();

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return File(JsonSerializer.SerializeToUtf8Bytes(rows, _json), "application/json", "vulnerabilities.json");
        }

        var csv = new StringBuilder();
        csv.Append("feed,package,version,advisory,aliases,severity,cvss_score,summary,affected_versions,fixed_version,found_at_utc\r\n");
        foreach (var row in rows)
        {
            csv.AppendJoin(',', new[]
            {
                row.feed,
                row.package,
                row.version,
                row.advisory,
                row.aliases,
                row.severity,
                row.cvssScore?.ToString("0.0", CultureInfo.InvariantCulture),
                row.summary,
                row.affectedVersions,
                row.fixedVersion,
                row.foundAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            }.Select(CsvField));
            csv.Append("\r\n");
        }

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "vulnerabilities.csv");
    }

    /// <summary>
    /// A CSV field: quoted when it has a comma, quote or line break. A field that starts with
    /// <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> gets a leading quote, so spreadsheets don't run it
    /// as a formula.
    /// </summary>
    public static string CsvField(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if ("=+-@\t\r".Contains(value[0], StringComparison.Ordinal))
        {
            value = "'" + value;
        }

        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private VulnerabilityFilter CreateFilter()
    {
        return new VulnerabilityFilter
        {
            FeedId = string.IsNullOrEmpty(Feed) ? null : Feeds.FirstOrDefault(f => f.Slug == Feed)?.Id ?? Guid.Empty,
            MinSeverity = Severity,
            Query = Query,
        };
    }

    private async Task LoadFeedsAsync(CancellationToken cancellationToken)
    {
        Feeds = await _feedService.GetAllFeedsAsync(cancellationToken);
        FeedsById = Feeds.ToDictionary(f => f.Id);
    }

    private async Task<bool> IsCurrentUserAdminAsync(CancellationToken cancellationToken)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null || !Guid.TryParse(claim.Value, out var userId)) return false;
        return await _userService.IsAdminAsync(userId, cancellationToken);
    }
}
