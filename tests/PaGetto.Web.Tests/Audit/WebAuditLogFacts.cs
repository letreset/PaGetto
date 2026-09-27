using System;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Audit;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Web.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace PaGetto.Web.Tests.Audit;

public class WebAuditLogFacts
{
    public class AdminAsync : FactsBase
    {
        [Fact]
        public async Task LogsActorTargetDetailAndIp()
        {
            await Target.AdminAsync(CreateContext("admin"), "account_disabled", "bob", "reason");

            Verify(LogLevel.Information, "AUDIT account_disabled target=bob detail=reason actor=admin ip=10.0.0.1");
        }

        [Fact]
        public async Task FallsBackToUserIdAndAnonymous()
        {
            var id = Guid.NewGuid();
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Test")),
            };
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

            await Target.AdminAsync(context, "feed_created", "internal");

            Verify(LogLevel.Information, $"AUDIT feed_created target=internal detail= actor={id} ip=10.0.0.1");
        }

        [Fact]
        public async Task StoresTheEvent()
        {
            await Target.AdminAsync(CreateContext("admin"), "group_updated", "Temp", "name=Temporary");

            AuditEvents.Verify(a => a.AddAsync(
                It.Is<AuditEvent>(e => e.Id != Guid.Empty && e.TimestampUtc == Now && e.Event == "group_updated"
                    && e.Target == "Temp" && e.Detail == "name=Temporary" && e.Actor == "admin" && e.IpAddress == "10.0.0.1"),
                CancellationToken.None), Times.Once);
        }

        [Fact]
        public async Task DoesNotFailWhenTheEventCantBeStored()
        {
            AuditEvents
                .Setup(a => a.AddAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("database is down"));

            await Target.AdminAsync(CreateContext("admin"), "account_created", "bob");

            Verify(LogLevel.Information, "AUDIT account_created target=bob detail= actor=admin ip=10.0.0.1");
        }
    }

    public class PackageAsync : FactsBase
    {
        [Fact]
        public async Task UsesTheNuGetApiFormat()
        {
            await Target.PackageAsync(CreateContext("alice"), LogLevel.Warning, "package_unlist_unauthorized", "default", "Foo", "1.0.0");

            Verify(LogLevel.Warning, "AUDIT package_unlist_unauthorized feed=default package_id=Foo package_version=1.0.0 actor=alice ip=10.0.0.1");
        }

        [Fact]
        public async Task StoresSuccessfulActions()
        {
            await Target.PackageAsync(CreateContext("alice"), LogLevel.Information, "package_unlist_succeeded", "default", "Foo", "1.0.0");

            AuditEvents.Verify(a => a.AddAsync(
                It.Is<AuditEvent>(e => e.Event == "package_unlist_succeeded" && e.Feed == "default"
                    && e.PackageId == "Foo" && e.PackageVersion == "1.0.0" && e.Actor == "alice"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task OnlyLogsFailedAttempts()
        {
            await Target.PackageAsync(CreateContext("alice"), LogLevel.Warning, "package_unlist_unauthorized", "default", "Foo", "1.0.0");

            AuditEvents.Verify(a => a.AddAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task UsesTheGivenActor()
        {
            await Target.PackageAsync(CreateContext("alice"), LogLevel.Information, "package_upload_succeeded", "default", "Foo", "1.0.0", "api-key");

            Verify(LogLevel.Information, "AUDIT package_upload_succeeded feed=default package_id=Foo package_version=1.0.0 actor=api-key ip=10.0.0.1");
        }
    }

    public class SymbolAsync : FactsBase
    {
        [Fact]
        public async Task KeepsTheSymbolFormat()
        {
            await Target.SymbolAsync(CreateContext("alice"), LogLevel.Warning, "symbol_upload_too_large", "default", "alice");

            Verify(LogLevel.Warning, "AUDIT symbol_upload_too_large feed=default actor=alice ip=10.0.0.1");
            AuditEvents.Verify(a => a.AddAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    public abstract class FactsBase
    {
        protected static readonly DateTime Now = new(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        protected readonly Mock<ILogger<WebAuditLog>> Logger = new();
        protected readonly Mock<IAuditEventService> AuditEvents = new();
        protected readonly WebAuditLog Target;

        protected FactsBase()
        {
            Logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            var time = new Mock<SystemTime>();
            time.Setup(t => t.UtcNow).Returns(Now);
            Target = TestWebAuditLog.Create(Logger.Object, AuditEvents.Object, time.Object);
        }

        protected static HttpContext CreateContext(string username)
        {
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, username)], "Test")),
            };
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
            return context;
        }

        protected void Verify(LogLevel level, string expected)
        {
            Logger.Verify(
                l => l.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) => state.ToString() == expected),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }
    }
}
