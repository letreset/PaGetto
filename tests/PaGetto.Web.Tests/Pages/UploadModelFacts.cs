using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Indexing;
using PaGetto.Web.Audit;
using PaGetto.Web.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace PaGetto.Web.Tests.Pages;

public class UploadModelFacts
{
    public class OnGetAsync : FactsBase
    {
        [Fact]
        public async Task ConfigModeShowsPage()
        {
            var target = CreateTarget(AuthenticationMode.Config, UnauthenticatedUser());

            Assert.IsType<PageResult>(await target.OnGetAsync(Ct));
            Assert.False(target.ApiKeyRequired);
        }

        [Fact]
        public async Task ConfigModeWithApiKeyAsksForIt()
        {
            ApiKeyConfigured = true;
            var target = CreateTarget(AuthenticationMode.Config, UnauthenticatedUser());

            Assert.IsType<PageResult>(await target.OnGetAsync(Ct));
            Assert.True(target.ApiKeyRequired);
        }

        [Fact]
        public async Task AnonymousVisitorGetsSignInPrompt()
        {
            var target = CreateTarget(AuthenticationMode.Local, UnauthenticatedUser());

            Assert.IsType<PageResult>(await target.OnGetAsync(Ct));
            Permissions.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UserWithoutPullGetsNotFound()
        {
            Permissions.Setup(p => p.CanPullAsync(UserId, FeedId, Ct)).ReturnsAsync(false);
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser());

            Assert.IsType<NotFoundResult>(await target.OnGetAsync(Ct));
        }

        [Fact]
        public async Task PullOnlyUserGetsNotFound()
        {
            AllowPull();
            Permissions.Setup(p => p.CanPushAsync(UserId, FeedId, Ct)).ReturnsAsync(false);
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser());

            Assert.IsType<NotFoundResult>(await target.OnGetAsync(Ct));
        }

        [Fact]
        public async Task UserWithPushSeesPage()
        {
            AllowPush();
            var target = CreateTarget(AuthenticationMode.Entra, SignedInUser());

            Assert.IsType<PageResult>(await target.OnGetAsync(Ct));
            Assert.False(target.IsReadOnly);
            Assert.Equal(8u, target.MaxPackageSizeGiB);
        }

        [Fact]
        public async Task ReadOnlyFeedIsFlagged()
        {
            AllowPush();
            FeedSettings.Setup(s => s.GetIsReadOnlyMode(It.IsAny<Feed>())).Returns(true);
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser());

            Assert.IsType<PageResult>(await target.OnGetAsync(Ct));
            Assert.True(target.IsReadOnly);
        }
    }

    public class OnGetCheckAsync : FactsBase
    {
        [Fact]
        public async Task ReportsAnExistingVersion()
        {
            AllowPush();
            Packages.Setup(p => p.ExistsAsync(FeedId, "Foo", NuGetVersion.Parse("1.0.0"), Ct)).ReturnsAsync(true);
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser());

            var result = Assert.IsType<JsonResult>(await target.OnGetCheckAsync("Foo", "1.0.0", Ct));

            Assert.Equal("{ exists = True, canOverwrite = False }", result.Value.ToString());
        }

        [Fact]
        public async Task PrereleaseOverwritesFollowTheFeedSetting()
        {
            AllowPush();
            FeedSettings.Setup(s => s.GetAllowPackageOverwrites(It.IsAny<Feed>())).Returns(PackageOverwriteAllowed.PrereleaseOnly);
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser());

            var result = Assert.IsType<JsonResult>(await target.OnGetCheckAsync("Foo", "1.0.0-beta", Ct));

            Assert.Equal("{ exists = False, canOverwrite = True }", result.Value.ToString());
        }

        [Fact]
        public async Task InvalidVersionIsBadRequest()
        {
            AllowPush();
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser());

            Assert.IsType<BadRequestResult>(await target.OnGetCheckAsync("Foo", "not-a-version", Ct));
        }

        [Fact]
        public async Task AnonymousVisitorIsUnauthorized()
        {
            var target = CreateTarget(AuthenticationMode.Local, UnauthenticatedUser());

            Assert.IsType<UnauthorizedResult>(await target.OnGetCheckAsync("Foo", "1.0.0", Ct));
            Packages.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task PullOnlyUserGetsNotFound()
        {
            AllowPull();
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser());

            Assert.IsType<NotFoundResult>(await target.OnGetCheckAsync("Foo", "1.0.0", Ct));
            Packages.VerifyNoOtherCalls();
        }
    }

    public class OnPostPackageAsync : FactsBase
    {
        [Fact]
        public async Task PublishesThePackage()
        {
            AllowPush();
            IndexReturns(PackageIndexingResult.Success);
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser(), body: CreatePackage("Foo", "1.0.0"));

            var result = await target.OnPostPackageAsync(Ct);

            AssertOutcome(result, 201, "published");
            VerifyAudit(LogLevel.Information, "package_upload_succeeded", "Foo", "1.0.0", "alice");
        }

        [Fact]
        public async Task PullOnlyUserIsRejected()
        {
            AllowPull();
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser(), body: CreatePackage("Foo", "1.0.0"));

            var result = await target.OnPostPackageAsync(Ct);

            AssertOutcome(result, 403, "unauthorized");
            VerifyAudit(LogLevel.Warning, "package_upload_unauthorized", null, null, "alice");
            Indexer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task AnonymousVisitorIsRejected()
        {
            var target = CreateTarget(AuthenticationMode.Local, UnauthenticatedUser(), body: CreatePackage("Foo", "1.0.0"));

            var result = await target.OnPostPackageAsync(Ct);

            AssertOutcome(result, 401, "unauthorized");
            VerifyAudit(LogLevel.Warning, "package_upload_unauthorized", null, null, "anonymous");
            Indexer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ReadOnlyFeedIsRejected()
        {
            AllowPush();
            FeedSettings.Setup(s => s.GetIsReadOnlyMode(It.IsAny<Feed>())).Returns(true);
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser(), body: CreatePackage("Foo", "1.0.0"));

            var result = await target.OnPostPackageAsync(Ct);

            AssertOutcome(result, 403, "read_only");
            VerifyAudit(LogLevel.Warning, "package_upload_read_only", null, null, "alice");
            Indexer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ConfigModeRejectsAWrongApiKey()
        {
            ApiKeyConfigured = true;
            var target = CreateTarget(AuthenticationMode.Config, UnauthenticatedUser(), apiKey: "wrong", body: CreatePackage("Foo", "1.0.0"));

            var result = await target.OnPostPackageAsync(Ct);

            AssertOutcome(result, 401, "unauthorized");
            VerifyAudit(LogLevel.Warning, "package_upload_unauthorized", null, null, "api-key");
            Indexer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ConfigModeAcceptsTheApiKey()
        {
            ApiKeyConfigured = true;
            IndexReturns(PackageIndexingResult.Success);
            var target = CreateTarget(AuthenticationMode.Config, UnauthenticatedUser(), apiKey: ValidApiKey, body: CreatePackage("Foo", "1.0.0"));

            var result = await target.OnPostPackageAsync(Ct);

            AssertOutcome(result, 201, "published");
            VerifyAudit(LogLevel.Information, "package_upload_succeeded", "Foo", "1.0.0", "api-key");
        }

        [Fact]
        public async Task TooLargePackageIsRejected()
        {
            AllowPush();
            FeedSettings.Setup(s => s.GetMaxPackageSizeGiB(It.IsAny<Feed>())).Returns(0u);
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser(), body: CreatePackage("Foo", "1.0.0"));

            var result = await target.OnPostPackageAsync(Ct);

            AssertOutcome(result, 413, "too_large");
            VerifyAudit(LogLevel.Warning, "package_upload_too_large", "Foo", "1.0.0", "alice");
            Indexer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ExistingVersionIsAConflict()
        {
            AllowPush();
            IndexReturns(PackageIndexingResult.PackageAlreadyExists);
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser(), body: CreatePackage("Foo", "1.0.0"));

            var result = await target.OnPostPackageAsync(Ct);

            AssertOutcome(result, 409, "exists");
            VerifyAudit(LogLevel.Warning, "package_upload_already_exists", "Foo", "1.0.0", "alice");
        }

        [Fact]
        public async Task InvalidPackageIsABadRequest()
        {
            AllowPush();
            IndexReturns(PackageIndexingResult.InvalidPackage);
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser(), body: new MemoryStream([1, 2, 3]));

            var result = await target.OnPostPackageAsync(Ct);

            AssertOutcome(result, 400, "invalid");
            VerifyAudit(LogLevel.Warning, "package_upload_invalid_package", null, null, "alice");
        }
    }

    public class OnPostSymbolAsync : FactsBase
    {
        [Fact]
        public async Task PublishesTheSymbols()
        {
            AllowPush();
            SymbolIndexer
                .Setup(i => i.IndexAsync(FeedId, "internal", It.IsAny<Stream>(), Ct))
                .ReturnsAsync(SymbolIndexingResult.Success);
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser(), body: CreatePackage("Foo", "1.0.0"));

            var result = await target.OnPostSymbolAsync(Ct);

            AssertOutcome(result, 201, "published");
            VerifyAudit(LogLevel.Information, "symbol_upload_succeeded", "Foo", "1.0.0", "alice");
        }

        [Fact]
        public async Task MissingPackageIsNotFound()
        {
            AllowPush();
            SymbolIndexer
                .Setup(i => i.IndexAsync(FeedId, "internal", It.IsAny<Stream>(), Ct))
                .ReturnsAsync(SymbolIndexingResult.PackageNotFound);
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser(), body: CreatePackage("Foo", "1.0.0"));

            var result = await target.OnPostSymbolAsync(Ct);

            AssertOutcome(result, 404, "not_found");
            VerifyAudit(LogLevel.Warning, "symbol_upload_package_not_found", "Foo", "1.0.0", "alice");
        }

        [Fact]
        public async Task PullOnlyUserIsRejected()
        {
            AllowPull();
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser(), body: CreatePackage("Foo", "1.0.0"));

            var result = await target.OnPostSymbolAsync(Ct);

            AssertOutcome(result, 403, "unauthorized");
            VerifyAudit(LogLevel.Warning, "symbol_upload_unauthorized", null, null, "alice");
            SymbolIndexer.VerifyNoOtherCalls();
        }
    }

    public abstract class FactsBase
    {
        protected const string ValidApiKey = "secret";

        protected readonly Mock<IPermissionService> Permissions = new();
        protected readonly Mock<IAuthenticationService> Authentication = new();
        protected readonly Mock<IFeedSettingsResolver> FeedSettings = new();
        protected readonly Mock<IPackageIndexingService> Indexer = new();
        protected readonly Mock<ISymbolIndexingService> SymbolIndexer = new();
        protected readonly Mock<IPackageDatabase> Packages = new();
        protected readonly Mock<ILogger<WebAuditLog>> AuditLogger = new();
        protected readonly Guid FeedId = Guid.NewGuid();
        protected readonly Guid UserId = Guid.NewGuid();
        protected readonly CancellationToken Ct = CancellationToken.None;

        protected FactsBase()
        {
            FeedSettings.Setup(s => s.GetMaxPackageSizeGiB(It.IsAny<Feed>())).Returns(8u);
            AuditLogger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

            // Like ApiKeyAuthenticationService: anything passes until a key is configured.
            Authentication
                .Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string apiKey, CancellationToken _) => !ApiKeyConfigured || apiKey == ValidApiKey);
        }

        protected bool ApiKeyConfigured { get; set; }

        protected void AllowPull()
        {
            Permissions.Setup(p => p.CanPullAsync(UserId, FeedId, Ct)).ReturnsAsync(true);
        }

        protected void AllowPush()
        {
            AllowPull();
            Permissions.Setup(p => p.CanPushAsync(UserId, FeedId, Ct)).ReturnsAsync(true);
        }

        protected void IndexReturns(PackageIndexingResult result)
        {
            Indexer
                .Setup(i => i.IndexAsync(FeedId, "internal", It.IsAny<Stream>(), null, null, Ct))
                .ReturnsAsync(result);
        }

        protected static ClaimsPrincipal UnauthenticatedUser()
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        protected ClaimsPrincipal SignedInUser()
        {
            return new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "alice"), new Claim(ClaimTypes.NameIdentifier, UserId.ToString()) }, "TestAuth"));
        }

        protected UploadModel CreateTarget(AuthenticationMode mode, ClaimsPrincipal user, string apiKey = null, Stream body = null)
        {
            var feedContext = new Mock<IFeedContext>();
            feedContext.Setup(f => f.CurrentFeed).Returns(new Feed { Id = FeedId, Slug = "internal", Name = "Internal" });

            var authOptions = new Mock<IOptionsSnapshot<NugetAuthenticationOptions>>();
            authOptions.Setup(o => o.Value).Returns(new NugetAuthenticationOptions { Mode = mode });

            var httpContext = new DefaultHttpContext { User = user };
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
            if (apiKey != null)
                httpContext.Request.Headers["X-NuGet-ApiKey"] = apiKey;
            if (body != null)
                httpContext.Request.Body = body;

            var url = new Mock<IUrlHelper>();
            url.Setup(u => u.ActionContext).Returns(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
            url.Setup(u => u.RouteUrl(It.IsAny<UrlRouteContext>())).Returns("/packages/foo/1.0.0");

            return new UploadModel(
                Permissions.Object,
                feedContext.Object,
                authOptions.Object,
                Authentication.Object,
                FeedSettings.Object,
                Indexer.Object,
                SymbolIndexer.Object,
                Packages.Object,
                new WebAuditLog(AuditLogger.Object),
                Mock.Of<ILogger<UploadModel>>())
            {
                PageContext = new PageContext { HttpContext = httpContext },
                Url = url.Object,
            };
        }

        protected static void AssertOutcome(IActionResult result, int statusCode, string outcome)
        {
            var json = Assert.IsType<JsonResult>(result);
            Assert.Equal(statusCode, json.StatusCode);
            Assert.Contains($"outcome = {outcome},", json.Value.ToString());
        }

        protected void VerifyAudit(LogLevel level, string eventName, string packageId, string packageVersion, string actor)
        {
            var expected = $"AUDIT {eventName} feed=internal package_id={packageId} " +
                $"package_version={packageVersion} actor={actor} ip=10.0.0.1";

            AuditLogger.Verify(
                l => l.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) => state.ToString() == expected),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
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
