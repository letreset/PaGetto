using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Audit;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PaGetto.Web.Pages.Admin;

[Authorize(AuthenticationSchemes = Core.Authentication.AuthenticationConstants.CookieScheme)]
public class AuditModel : PageModel
{
    public const int PageSize = 50;

    private readonly IAuditEventService _auditEvents;
    private readonly IUserService _userService;
    private readonly IFeedService _feedService;

    public AuditModel(IAuditEventService auditEvents, IUserService userService, IFeedService feedService)
    {
        _auditEvents = auditEvents ?? throw new ArgumentNullException(nameof(auditEvents));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _feedService = feedService ?? throw new ArgumentNullException(nameof(feedService));
    }

    [FromQuery(Name = "event")]
    public string Event { get; set; }

    [FromQuery(Name = "actor")]
    public string Actor { get; set; }

    [FromQuery(Name = "feed")]
    public string Feed { get; set; }

    [FromQuery(Name = "target")]
    public string Target { get; set; }

    /// <summary>
    /// The first day to show, in UTC.
    /// </summary>
    [FromQuery(Name = "from")]
    public DateTime? From { get; set; }

    /// <summary>
    /// The last day to show, in UTC.
    /// </summary>
    [FromQuery(Name = "to")]
    public DateTime? To { get; set; }

    [FromQuery(Name = "p")]
    public int PageIndex { get; set; } = 1;

    public List<AuditEvent> Events { get; private set; } = new();
    public int TotalCount { get; private set; }
    public int TotalPages { get; private set; } = 1;
    public List<string> EventNames { get; private set; } = new();
    public List<Feed> Feeds { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        PageIndex = Math.Max(1, PageIndex);

        var filter = new AuditEventFilter
        {
            Event = Event,
            Actor = Actor?.Trim(),
            Feed = Feed,
            Target = Target?.Trim(),
            FromUtc = From.HasValue ? DateTime.SpecifyKind(From.Value.Date, DateTimeKind.Utc) : null,
            BeforeUtc = To.HasValue ? DateTime.SpecifyKind(To.Value.Date.AddDays(1), DateTimeKind.Utc) : null,
        };

        (Events, TotalCount) = await _auditEvents.SearchAsync(filter, (PageIndex - 1) * PageSize, PageSize, cancellationToken);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        EventNames = await _auditEvents.GetEventNamesAsync(cancellationToken);
        Feeds = await _feedService.GetAllFeedsAsync(cancellationToken);

        return Page();
    }

    private async Task<bool> IsCurrentUserAdminAsync(CancellationToken cancellationToken)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null || !Guid.TryParse(claim.Value, out var userId)) return false;
        return await _userService.IsAdminAsync(userId, cancellationToken);
    }
}
