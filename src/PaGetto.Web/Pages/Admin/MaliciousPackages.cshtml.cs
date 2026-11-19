using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.MaliciousPackages;
using PaGetto.Core.Vulnerabilities;
using PaGetto.Web.Audit;

namespace PaGetto.Web.Pages.Admin;

/// <summary>
/// The local copy of the malicious packages data, a lookup of a package in it, and the packages an
/// administrator allows although it lists them.
/// </summary>
[Authorize(AuthenticationSchemes = AuthenticationConstants.CookieScheme)]
public class MaliciousPackagesModel : PageModel
{
    /// <summary>
    /// The OSV ecosystems the malicious packages data can be checked for.
    /// </summary>
    public static readonly IReadOnlyList<string> Ecosystems = OsvEcosystem.SupportedFeedTypes
        .Select(OsvEcosystem.GetName)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    private readonly MaliciousPackageService _malicious;
    private readonly IUserService _userService;
    private readonly WebAuditLog _audit;
    private readonly IOptionsSnapshot<MaliciousPackageOptions> _options;

    public MaliciousPackagesModel(
        MaliciousPackageService malicious,
        IUserService userService,
        WebAuditLog audit,
        IOptionsSnapshot<MaliciousPackageOptions> options)
    {
        _malicious = malicious ?? throw new ArgumentNullException(nameof(malicious));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// The ecosystem of the package to look up.
    /// </summary>
    [FromQuery(Name = "ecosystem")]
    public string LookupEcosystem { get; set; }

    /// <summary>
    /// The name of the package to look up.
    /// </summary>
    [FromQuery(Name = "name")]
    public string LookupName { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Choose an ecosystem.")]
    public string AllowEcosystem { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "The package name is required.")]
    [MaxLength(256)]
    public string AllowName { get; set; }

    [BindProperty]
    [MaxLength(128)]
    public string AllowVersion { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Say why the package is allowed.")]
    [MaxLength(1000)]
    public string AllowReason { get; set; }

    public bool DefaultEnabled => _options.Value.Enabled;

    public IReadOnlyList<MaliciousPackageSourceStatus> Sources { get; private set; } = [];

    public IReadOnlyList<MaliciousPackageAllowance> Allowances { get; private set; } = [];

    /// <summary>
    /// The listings of the looked up package, or null when none was looked up.
    /// </summary>
    public IReadOnlyList<MaliciousPackage> Listings { get; private set; }

    public string SuccessMessage { get; set; }

    /// <summary>
    /// The allowance form's properties are bound (and validated) for every POST; only the Allow
    /// handler uses them.
    /// </summary>
    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if (context.HandlerMethod?.MethodInfo.Name != nameof(OnPostAllowAsync))
        {
            ModelState.Clear();
        }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAllowAsync(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        if (AllowEcosystem != null && !Ecosystems.Contains(AllowEcosystem))
        {
            ModelState.AddModelError(nameof(AllowEcosystem), "Choose an ecosystem.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var allowance = await _malicious.AddAllowanceAsync(AllowEcosystem, AllowName, AllowVersion, AllowReason, User.Identity?.Name, cancellationToken);
        await _audit.AdminAsync(HttpContext, "malicious_package_allowed", Describe(allowance), "reason=" + allowance.Reason);

        SuccessMessage = $"{Describe(allowance)} is allowed.";
        AllowName = AllowVersion = AllowReason = null;
        ModelState.Clear();

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int key, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        var allowance = await _malicious.RemoveAllowanceAsync(key, cancellationToken);
        if (allowance != null)
        {
            await _audit.AdminAsync(HttpContext, "malicious_package_allowance_removed", Describe(allowance));
            SuccessMessage = $"{Describe(allowance)} is no longer allowed.";
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    /// <summary>
    /// An allowance as the page and the audit log name it, e.g. <c>npm:left-pad@1.0.0</c>.
    /// </summary>
    public static string Describe(MaliciousPackageAllowance allowance)
    {
        ArgumentNullException.ThrowIfNull(allowance);

        return $"{allowance.Ecosystem}:{allowance.Name}" + (allowance.Version == null ? string.Empty : "@" + allowance.Version);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Sources = await _malicious.GetSourcesAsync(cancellationToken);
        Allowances = await _malicious.GetAllowancesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(LookupName) && LookupEcosystem != null && Ecosystems.Contains(LookupEcosystem))
        {
            Listings = await _malicious.FindListingsAsync(LookupEcosystem, LookupName, cancellationToken);
        }
    }

    private async Task<bool> IsCurrentUserAdminAsync(CancellationToken cancellationToken)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null || !Guid.TryParse(claim.Value, out var userId)) return false;
        return await _userService.IsAdminAsync(userId, cancellationToken);
    }
}
