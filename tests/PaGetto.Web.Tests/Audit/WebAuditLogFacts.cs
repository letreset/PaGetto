using System;
using System.Net;
using System.Security.Claims;
using PaGetto.Web.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace PaGetto.Web.Tests.Audit;

public class WebAuditLogFacts
{
    public class Admin : FactsBase
    {
        [Fact]
        public void LogsActorTargetDetailAndIp()
        {
            Target.Admin(CreateContext("admin"), "account_disabled", "bob", "reason");

            Verify(LogLevel.Information, "AUDIT account_disabled target=bob detail=reason actor=admin ip=10.0.0.1");
        }

        [Fact]
        public void FallsBackToUserIdAndAnonymous()
        {
            var id = Guid.NewGuid();
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Test")),
            };
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

            Target.Admin(context, "feed_created", "internal");

            Verify(LogLevel.Information, $"AUDIT feed_created target=internal detail= actor={id} ip=10.0.0.1");
        }
    }

    public class Package : FactsBase
    {
        [Fact]
        public void UsesTheNuGetApiFormat()
        {
            Target.Package(CreateContext("alice"), LogLevel.Warning, "package_unlist_unauthorized", "default", "Foo", "1.0.0");

            Verify(LogLevel.Warning, "AUDIT package_unlist_unauthorized feed=default package_id=Foo package_version=1.0.0 actor=alice ip=10.0.0.1");
        }
    }

    public abstract class FactsBase
    {
        protected readonly Mock<ILogger<WebAuditLog>> Logger = new();
        protected readonly WebAuditLog Target;

        protected FactsBase()
        {
            Logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            Target = new WebAuditLog(Logger.Object);
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
