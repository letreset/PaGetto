using System;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Audit;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PaGetto.Web.Audit;

/// <summary>
/// Writes the <c>AUDIT</c> lines for package actions in the web UI and the NuGet API, symbol
/// uploads and administration changes. Package lines have the same format wherever they come from,
/// so they can be collected with one pattern. Successful actions (logged at Information) are also
/// stored for the admin audit page; failed and rejected attempts are only logged.
/// </summary>
public partial class WebAuditLog
{
    private readonly ILogger<WebAuditLog> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SystemTime _systemTime;

    public WebAuditLog(ILogger<WebAuditLog> logger, IServiceScopeFactory scopeFactory, SystemTime systemTime)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _systemTime = systemTime ?? throw new ArgumentNullException(nameof(systemTime));
    }

    /// <summary>
    /// Logs a package change, e.g. <c>package_unlist_succeeded</c>. <paramref name="actor"/>
    /// overrides the signed-in user, e.g. <c>api-key</c> for a Config mode upload.
    /// </summary>
    public async Task PackageAsync(HttpContext httpContext, LogLevel level, string eventName, string feed, string packageId, string packageVersion, string actor = null)
    {
        actor ??= GetActor(httpContext);
        var ip = httpContext.Connection.RemoteIpAddress;
        LogPackageEvent(level, eventName, feed, packageId, packageVersion, actor, ip);

        if (level == LogLevel.Information)
        {
            await StoreAsync(new AuditEvent
            {
                Event = eventName,
                Actor = actor,
                IpAddress = ip?.ToString(),
                Feed = feed,
                PackageId = packageId,
                PackageVersion = packageVersion,
            });
        }
    }

    /// <summary>
    /// Logs a symbol package upload through the NuGet API, e.g. <c>symbol_upload_too_large</c>.
    /// </summary>
    public async Task SymbolAsync(HttpContext httpContext, LogLevel level, string eventName, string feed, string actor)
    {
        var ip = httpContext.Connection.RemoteIpAddress;
        LogSymbolEvent(level, eventName, feed, actor, ip);

        if (level == LogLevel.Information)
        {
            await StoreAsync(new AuditEvent
            {
                Event = eventName,
                Actor = actor,
                IpAddress = ip?.ToString(),
                Feed = feed,
            });
        }
    }

    /// <summary>
    /// Logs an administration change, e.g. <c>account_disabled</c>. <paramref name="target"/> names
    /// what was changed (a username, group name or feed slug), <paramref name="detail"/> optionally
    /// says how.
    /// </summary>
    public async Task AdminAsync(HttpContext httpContext, string eventName, string target, string detail = null)
    {
        var actor = GetActor(httpContext);
        var ip = httpContext.Connection.RemoteIpAddress;
        LogAdminEvent(eventName, target, detail ?? string.Empty, actor, ip);

        await StoreAsync(new AuditEvent
        {
            Event = eventName,
            Actor = actor,
            IpAddress = ip?.ToString(),
            Target = target,
            Detail = detail,
        });
    }

    private async Task StoreAsync(AuditEvent auditEvent)
    {
        auditEvent.Id = Guid.NewGuid();
        auditEvent.TimestampUtc = _systemTime.UtcNow;

        try
        {
            // A scope of its own, so saving the event never saves changes the caller hasn't saved yet.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var auditEvents = scope.ServiceProvider.GetRequiredService<IAuditEventService>();

            // The change has already been made, so a client that leaves now doesn't cancel its record.
            await auditEvents.AddAsync(auditEvent, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The log line is written; don't fail a change that succeeded because its record couldn't be stored.
            LogStoreFailed(ex, auditEvent.Event);
        }
    }

    private static string GetActor(HttpContext httpContext)
    {
        var user = httpContext.User;
        var name = user.Identity?.Name;
        if (!string.IsNullOrEmpty(name))
            return name;

        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
    }

    [LoggerMessage(Message = "AUDIT {Event} feed={Feed} package_id={PackageId} package_version={PackageVersion} actor={Actor} ip={Ip}")]
    private partial void LogPackageEvent(LogLevel level, string @event, string feed, string packageId, string packageVersion, string actor, IPAddress ip);

    [LoggerMessage(Message = "AUDIT {Event} feed={Feed} actor={Actor} ip={Ip}")]
    private partial void LogSymbolEvent(LogLevel level, string @event, string feed, string actor, IPAddress ip);

    [LoggerMessage(Level = LogLevel.Information, Message = "AUDIT {Event} target={Target} detail={Detail} actor={Actor} ip={Ip}")]
    private partial void LogAdminEvent(string @event, string target, string detail, string actor, IPAddress ip);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't store the audit event {Event}")]
    private partial void LogStoreFailed(Exception exception, string @event);
}
