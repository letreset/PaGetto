using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Indexing;
using PaGetto.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace PaGetto.Web.Tests.Controllers;

public class PackagePublishControllerFacts
{
    public class Upload : FactsBase
    {
        [Fact]
        public async Task LogsSucceeded()
        {
            Indexer
                .Setup(i => i.IndexAsync(Feed.Id, Feed.Slug, It.IsAny<Stream>(), null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(PackageIndexingResult.Success);
            var controller = Build(apiKey: ValidApiKey, body: CreatePackage("Foo", "1.0.0"));

            await controller.Upload(CancellationToken.None);

            Assert.Equal(201, controller.Response.StatusCode);
            VerifyAudit(LogLevel.Information, "package_upload_succeeded", "Foo", "1.0.0", "api-key");
        }

        [Fact]
        public async Task LogsUnauthorized()
        {
            var controller = Build(apiKey: "wrong", body: CreatePackage("Foo", "1.0.0"));

            await controller.Upload(CancellationToken.None);

            Assert.Equal(401, controller.Response.StatusCode);
            VerifyAudit(LogLevel.Warning, "package_upload_unauthorized", null, null, "api-key");
        }

        [Fact]
        public async Task LogsReadOnly()
        {
            FeedSettings.Setup(s => s.GetIsReadOnlyMode(Feed)).Returns(true);
            var controller = Build(apiKey: ValidApiKey, body: CreatePackage("Foo", "1.0.0"));

            await controller.Upload(CancellationToken.None);

            Assert.Equal(403, controller.Response.StatusCode);
            VerifyAudit(LogLevel.Warning, "package_upload_read_only", null, null, "api-key");
        }

        [Fact]
        public async Task LogsTooLargeForFeed()
        {
            FeedSettings.Setup(s => s.GetMaxPackageSizeGiB(Feed)).Returns(0);
            var controller = Build(apiKey: ValidApiKey, body: CreatePackage("Foo", "1.0.0"));

            await controller.Upload(CancellationToken.None);

            Assert.Equal(413, controller.Response.StatusCode);
            VerifyAudit(LogLevel.Warning, "package_upload_too_large", "Foo", "1.0.0", "api-key");
            Indexer.Verify(
                i => i.IndexAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task LogsAlreadyExists()
        {
            Indexer
                .Setup(i => i.IndexAsync(Feed.Id, Feed.Slug, It.IsAny<Stream>(), null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(PackageIndexingResult.PackageAlreadyExists);
            var controller = Build(apiKey: ValidApiKey, body: CreatePackage("Foo", "1.0.0"));

            await controller.Upload(CancellationToken.None);

            Assert.Equal(409, controller.Response.StatusCode);
            VerifyAudit(LogLevel.Warning, "package_upload_already_exists", "Foo", "1.0.0", "api-key");
        }

        [Fact]
        public async Task LogsInvalidPackage()
        {
            Indexer
                .Setup(i => i.IndexAsync(Feed.Id, Feed.Slug, It.IsAny<Stream>(), null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(PackageIndexingResult.InvalidPackage);
            var controller = Build(apiKey: ValidApiKey, body: new MemoryStream(Encoding.UTF8.GetBytes("not a package")));

            await controller.Upload(CancellationToken.None);

            Assert.Equal(400, controller.Response.StatusCode);
            VerifyAudit(LogLevel.Warning, "package_upload_invalid_package", null, null, "api-key");
        }

        [Fact]
        public async Task LogsTokenOwnerAsActorForPatRequests()
        {
            UseLocalMode();
            var userId = Guid.NewGuid();
            FeedAuthentication
                .Setup(a => a.AuthenticateByTokenAsync("pat", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AuthResult(true, userId, "alice"));
            Permissions.Setup(p => p.CanPushAsync(userId, Feed.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Indexer
                .Setup(i => i.IndexAsync(Feed.Id, Feed.Slug, It.IsAny<Stream>(), null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(PackageIndexingResult.Success);
            var controller = Build(apiKey: "pat", body: CreatePackage("Foo", "1.0.0"));

            await controller.Upload(CancellationToken.None);

            VerifyAudit(LogLevel.Information, "package_upload_succeeded", "Foo", "1.0.0", "alice");
        }

        [Fact]
        public async Task LogsAuthenticatedUserAsActor()
        {
            UseLocalMode();
            var userId = Guid.NewGuid();
            Permissions.Setup(p => p.CanPushAsync(userId, Feed.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var controller = Build(user: CreateUser("bob", userId), body: CreatePackage("Foo", "1.0.0"));

            await controller.Upload(CancellationToken.None);

            VerifyAudit(LogLevel.Warning, "package_upload_unauthorized", null, null, "bob");
        }
    }

    public class Delete : FactsBase
    {
        [Fact]
        public async Task LogsSucceeded()
        {
            Deletion
                .Setup(d => d.TryDeletePackageAsync(Feed.Id, Feed.Slug, "Foo", NuGetVersion.Parse("1.0.0"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var result = await Build(apiKey: ValidApiKey).Delete("Foo", "1.0.0", CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            VerifyAudit(LogLevel.Information, "package_delete_succeeded", "Foo", "1.0.0", "api-key");
        }

        [Fact]
        public async Task LogsUnauthorized()
        {
            var result = await Build().Delete("Foo", "1.0.0", CancellationToken.None);

            Assert.IsType<UnauthorizedResult>(result);
            VerifyAudit(LogLevel.Warning, "package_delete_unauthorized", "Foo", "1.0.0", "anonymous");
        }

        [Fact]
        public async Task LogsReadOnly()
        {
            FeedSettings.Setup(s => s.GetIsReadOnlyMode(Feed)).Returns(true);

            var result = await Build(apiKey: ValidApiKey).Delete("Foo", "1.0.0", CancellationToken.None);

            Assert.Equal(403, Assert.IsType<StatusCodeResult>(result).StatusCode);
            VerifyAudit(LogLevel.Warning, "package_delete_read_only", "Foo", "1.0.0", "api-key");
        }

        [Fact]
        public async Task LogsNotFound()
        {
            var result = await Build(apiKey: ValidApiKey).Delete("Foo", "1.0.0", CancellationToken.None);

            Assert.IsType<NotFoundResult>(result);
            VerifyAudit(LogLevel.Warning, "package_delete_not_found", "Foo", "1.0.0", "api-key");
        }
    }

    public class Relist : FactsBase
    {
        [Fact]
        public async Task LogsSucceeded()
        {
            Packages
                .Setup(p => p.RelistPackageAsync(Feed.Id, "Foo", NuGetVersion.Parse("1.0.0"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var result = await Build(apiKey: ValidApiKey).Relist("Foo", "1.0.0", CancellationToken.None);

            Assert.IsType<OkResult>(result);
            VerifyAudit(LogLevel.Information, "package_relist_succeeded", "Foo", "1.0.0", "api-key");
        }

        [Fact]
        public async Task LogsUnauthorized()
        {
            var result = await Build().Relist("Foo", "1.0.0", CancellationToken.None);

            Assert.IsType<UnauthorizedResult>(result);
            VerifyAudit(LogLevel.Warning, "package_relist_unauthorized", "Foo", "1.0.0", "anonymous");
        }

        [Fact]
        public async Task LogsReadOnly()
        {
            FeedSettings.Setup(s => s.GetIsReadOnlyMode(Feed)).Returns(true);

            var result = await Build(apiKey: ValidApiKey).Relist("Foo", "1.0.0", CancellationToken.None);

            Assert.Equal(403, Assert.IsType<StatusCodeResult>(result).StatusCode);
            VerifyAudit(LogLevel.Warning, "package_relist_read_only", "Foo", "1.0.0", "api-key");
        }

        [Fact]
        public async Task LogsNotFound()
        {
            var result = await Build(apiKey: ValidApiKey).Relist("Foo", "1.0.0", CancellationToken.None);

            Assert.IsType<NotFoundResult>(result);
            VerifyAudit(LogLevel.Warning, "package_relist_not_found", "Foo", "1.0.0", "api-key");
        }
    }

    public class FactsBase
    {
        protected const string ValidApiKey = "secret";

        protected readonly Feed Feed = new() { Id = Guid.NewGuid(), Slug = "default" };
        protected readonly PaGettoOptions Options = new();
        protected readonly Mock<IAuthenticationService> Authentication = new();
        protected readonly Mock<IFeedAuthenticationService> FeedAuthentication = new();
        protected readonly Mock<IPermissionService> Permissions = new();
        protected readonly Mock<IFeedContext> FeedContext = new();
        protected readonly Mock<IFeedSettingsResolver> FeedSettings = new();
        protected readonly Mock<IPackageIndexingService> Indexer = new();
        protected readonly Mock<IPackageDatabase> Packages = new();
        protected readonly Mock<IPackageDeletionService> Deletion = new();
        protected readonly Mock<ILogger<PackagePublishController>> Logger = new();

        protected FactsBase()
        {
            FeedContext.Setup(f => f.CurrentFeed).Returns(Feed);
            FeedSettings.Setup(s => s.GetMaxPackageSizeGiB(Feed)).Returns(8);
            Logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            Authentication
                .Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string apiKey, CancellationToken _) => apiKey == ValidApiKey);
            FeedAuthentication
                .Setup(a => a.AuthenticateByTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AuthResult(false, null, null));
        }

        protected void UseLocalMode()
        {
            Options.Authentication = new NugetAuthenticationOptions { Mode = AuthenticationMode.Local };
        }

        protected PackagePublishController Build(string apiKey = null, ClaimsPrincipal user = null, Stream body = null)
        {
            var options = new Mock<IOptionsSnapshot<PaGettoOptions>>();
            options.Setup(o => o.Value).Returns(Options);

            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
            if (apiKey != null)
                httpContext.Request.Headers["X-NuGet-ApiKey"] = apiKey;
            if (user != null)
                httpContext.User = user;
            if (body != null)
                httpContext.Request.Body = body;

            return new PackagePublishController(
                Authentication.Object,
                FeedAuthentication.Object,
                Permissions.Object,
                FeedContext.Object,
                FeedSettings.Object,
                Indexer.Object,
                Packages.Object,
                Deletion.Object,
                options.Object,
                Logger.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
            };
        }

        protected void VerifyAudit(LogLevel level, string eventName, string packageId, string packageVersion, string actor)
        {
            var expected = $"AUDIT {eventName} feed=default package_id={packageId} " +
                $"package_version={packageVersion} actor={actor} ip=10.0.0.1";

            Logger.Verify(
                l => l.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) => state.ToString() == expected),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        protected static ClaimsPrincipal CreateUser(string name, Guid userId)
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                "Test");
            return new ClaimsPrincipal(identity);
        }

        protected static Stream CreatePackage(string id, string version)
        {
            var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry($"{id}.nuspec");
                using var writer = new StreamWriter(entry.Open());
                writer.Write(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">" +
                    $"<metadata><id>{id}</id><version>{version}</version><authors>test</authors><description>test</description></metadata>" +
                    "</package>");
            }

            stream.Position = 0;
            return stream;
        }
    }
}
