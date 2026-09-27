using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PaGetto.Web.Audit;

/// <summary>
/// Writes the <c>AUDIT</c> lines for changes made in the web UI: package actions on the package
/// page and administration changes. Package lines use the same format as the NuGet API's lines
/// (see <c>PackagePublishController</c>), so both can be collected with one pattern.
/// </summary>
public partial class WebAuditLog
{
    private readonly ILogger<WebAuditLog> _logger;

    public WebAuditLog(ILogger<WebAuditLog> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Logs a package change, e.g. <c>package_unlist_succeeded</c>.
    /// </summary>
    public void Package(HttpContext httpContext, LogLevel level, string eventName, string feed, string packageId, string packageVersion)
    {
        if (!_logger.IsEnabled(level))
            return;

        var actor = GetActor(httpContext);
        LogPackageEvent(level, eventName, feed, packageId, packageVersion, actor, httpContext.Connection.RemoteIpAddress);
    }

    /// <summary>
    /// Logs an administration change, e.g. <c>account_disabled</c>. <paramref name="target"/> names
    /// what was changed (a username, group name or feed slug), <paramref name="detail"/> optionally
    /// says how.
    /// </summary>
    public void Admin(HttpContext httpContext, string eventName, string target, string detail = null)
    {
        if (!_logger.IsEnabled(LogLevel.Information))
            return;

        var actor = GetActor(httpContext);
        LogAdminEvent(eventName, target, detail ?? string.Empty, actor, httpContext.Connection.RemoteIpAddress);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "AUDIT {Event} target={Target} detail={Detail} actor={Actor} ip={Ip}")]
    private partial void LogAdminEvent(string @event, string target, string detail, string actor, IPAddress ip);
}
