using PaGetto.Core.Audit;
using PaGetto.Core.Extensions;
using PaGetto.Web.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace PaGetto.Web.Tests.Audit;

/// <summary>
/// Builds a <see cref="WebAuditLog"/> that stores its events in <paramref name="auditEvents"/>, or nowhere.
/// </summary>
public static class TestWebAuditLog
{
    public static WebAuditLog Create(
        ILogger<WebAuditLog> logger = null,
        IAuditEventService auditEvents = null,
        SystemTime systemTime = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(auditEvents ?? Mock.Of<IAuditEventService>());

        return new WebAuditLog(
            logger ?? NullLogger<WebAuditLog>.Instance,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            systemTime ?? new SystemTime());
    }
}
