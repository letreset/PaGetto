using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Web.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Moq;
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
            Permissions.Setup(p => p.CanPullAsync(UserId, FeedId, Ct)).ReturnsAsync(true);
            Permissions.Setup(p => p.CanPushAsync(UserId, FeedId, Ct)).ReturnsAsync(false);
            var target = CreateTarget(AuthenticationMode.Local, SignedInUser());

            Assert.IsType<NotFoundResult>(await target.OnGetAsync(Ct));
        }

        [Fact]
        public async Task UserWithPushSeesPage()
        {
            Permissions.Setup(p => p.CanPullAsync(UserId, FeedId, Ct)).ReturnsAsync(true);
            Permissions.Setup(p => p.CanPushAsync(UserId, FeedId, Ct)).ReturnsAsync(true);
            var target = CreateTarget(AuthenticationMode.Entra, SignedInUser());

            Assert.IsType<PageResult>(await target.OnGetAsync(Ct));
        }
    }

    public abstract class FactsBase
    {
        protected readonly Mock<IPermissionService> Permissions = new();
        protected readonly Guid FeedId = Guid.NewGuid();
        protected readonly Guid UserId = Guid.NewGuid();
        protected readonly CancellationToken Ct = CancellationToken.None;

        protected static ClaimsPrincipal UnauthenticatedUser()
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        protected ClaimsPrincipal SignedInUser()
        {
            return new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, UserId.ToString()) }, "TestAuth"));
        }

        protected UploadModel CreateTarget(AuthenticationMode mode, ClaimsPrincipal user)
        {
            var feedContext = new Mock<IFeedContext>();
            feedContext.Setup(f => f.CurrentFeed).Returns(new Feed { Id = FeedId, Slug = "internal", Name = "Internal" });

            var authOptions = new Mock<IOptionsSnapshot<NugetAuthenticationOptions>>();
            authOptions.Setup(o => o.Value).Returns(new NugetAuthenticationOptions { Mode = mode });

            return new UploadModel(Permissions.Object, feedContext.Object, authOptions.Object)
            {
                PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = user } },
            };
        }
    }
}
