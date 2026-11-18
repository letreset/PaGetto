using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Web.Authentication;
using Xunit;

namespace PaGetto.Web.Tests.Authentication;

public class FeedChangeAuthorizerFacts
{
    public class CheckAsync : FactsBase
    {
        [Fact]
        public async Task LegacyModeChecksTheApiKey()
        {
            Mode = AuthenticationMode.Legacy;
            Authentication.Setup(a => a.AuthenticateAsync("good", Ct)).ReturnsAsync(true);

            Assert.Equal(FeedChangeAccess.Allowed, await CheckAsync(Anonymous(), "good", FeedPermissionRequirement.Push));
            Assert.Equal(FeedChangeAccess.Unauthorized, await CheckAsync(Anonymous(), "bad", FeedPermissionRequirement.Push));
            Permissions.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(AuthenticationMode.Local)]
        [InlineData(AuthenticationMode.Entra)]
        [InlineData(AuthenticationMode.Hybrid)]
        public async Task UserModesCheckThePermissionAskedFor(AuthenticationMode mode)
        {
            Mode = mode;
            var userId = Guid.NewGuid();
            Permissions.Setup(p => p.CanPushAsync(userId, Feed.Id, Ct)).ReturnsAsync(true);
            Permissions.Setup(p => p.CanDeleteAsync(userId, Feed.Id, Ct)).ReturnsAsync(false);

            Assert.Equal(FeedChangeAccess.Allowed, await CheckAsync(User(userId), apiKey: null, FeedPermissionRequirement.Push));
            Assert.Equal(FeedChangeAccess.Unauthorized, await CheckAsync(User(userId), apiKey: null, FeedPermissionRequirement.Delete));
        }

        [Fact]
        public async Task UserModesIgnoreTheApiKeyAndRefuseAnonymousCallers()
        {
            Mode = AuthenticationMode.Local;
            Authentication.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), Ct)).ReturnsAsync(true);

            Assert.Equal(FeedChangeAccess.Unauthorized, await CheckAsync(Anonymous(), "key", FeedPermissionRequirement.Push));
        }

        [Fact]
        public async Task ReadOnlyFeedIsReportedOnlyToAuthorizedCallers()
        {
            Mode = AuthenticationMode.Legacy;
            FeedSettings.Setup(s => s.GetIsReadOnlyMode(Feed)).Returns(true);
            Authentication.Setup(a => a.AuthenticateAsync("good", Ct)).ReturnsAsync(true);

            Assert.Equal(FeedChangeAccess.ReadOnly, await CheckAsync(Anonymous(), "good", FeedPermissionRequirement.Push));
            Assert.Equal(FeedChangeAccess.Unauthorized, await CheckAsync(Anonymous(), "bad", FeedPermissionRequirement.Push));
        }
    }

    public class FactsBase
    {
        protected readonly Feed Feed = new() { Id = Guid.NewGuid(), Slug = "feed" };
        protected readonly Mock<IFeedSettingsResolver> FeedSettings = new();
        protected readonly Mock<IAuthenticationService> Authentication = new();
        protected readonly Mock<IPermissionService> Permissions = new();
        protected readonly CancellationToken Ct = CancellationToken.None;
        protected AuthenticationMode Mode;

        protected Task<FeedChangeAccess> CheckAsync(ClaimsPrincipal user, string apiKey, string permission)
        {
            var feedContext = new Mock<IFeedContext>();
            feedContext.Setup(f => f.CurrentFeed).Returns(Feed);

            var options = new Mock<IOptionsSnapshot<PaGettoOptions>>();
            options.Setup(o => o.Value).Returns(new PaGettoOptions { Authentication = new NugetAuthenticationOptions { Mode = Mode } });

            var target = new FeedChangeAuthorizer(feedContext.Object, FeedSettings.Object, Authentication.Object, Permissions.Object, options.Object);
            return target.CheckAsync(user, apiKey, permission, Ct);
        }

        protected static ClaimsPrincipal Anonymous()
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        protected static ClaimsPrincipal User(Guid userId)
        {
            return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "TestAuth"));
        }
    }
}
