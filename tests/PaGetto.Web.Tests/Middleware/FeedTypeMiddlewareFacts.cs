using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Web.Middleware;
using Xunit;

namespace PaGetto.Web.Tests.Middleware;

public class FeedTypeMiddlewareFacts
{
    public class InvokeAsync
    {
        private readonly FeedContext _feedContext = new();
        private bool _nextCalled;

        private async Task<HttpContext> RunAsync(
            FeedType feedType,
            FeedType? endpointType,
            AuthenticationMode authMode = AuthenticationMode.Legacy,
            bool authenticated = false)
        {
            var context = new DefaultHttpContext();
            if (authenticated)
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "user")], "Test"));
            }

            var metadata = endpointType.HasValue
                ? new EndpointMetadataCollection(new FeedTypeAttribute(endpointType.Value))
                : EndpointMetadataCollection.Empty;
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test"));

            _feedContext.Set(new Feed { Slug = "feed", Type = feedType }, isDefaultRoute: false);

            var options = new Mock<IOptionsSnapshot<PaGettoOptions>>();
            options.Setup(o => o.Value).Returns(new PaGettoOptions
            {
                Authentication = new NugetAuthenticationOptions { Mode = authMode },
            });

            var middleware = new FeedTypeMiddleware(_ =>
            {
                _nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context, _feedContext, options.Object);
            return context;
        }

        [Theory]
        [InlineData(FeedType.NuGet, FeedType.NuGet)]
        [InlineData(FeedType.Pub, FeedType.Pub)]
        public async Task EndpointOfTheFeedsTypeGoesOn(FeedType feedType, FeedType endpointType)
        {
            await RunAsync(feedType, endpointType);

            Assert.True(_nextCalled);
        }

        [Theory]
        [InlineData(FeedType.NuGet, FeedType.Pub)]
        [InlineData(FeedType.Pub, FeedType.NuGet)]
        public async Task EndpointOfAnotherTypeIsNotFound(FeedType feedType, FeedType endpointType)
        {
            var context = await RunAsync(feedType, endpointType);

            Assert.False(_nextCalled);
            Assert.Equal(404, context.Response.StatusCode);
        }

        [Theory]
        [InlineData(FeedType.NuGet)]
        [InlineData(FeedType.Pub)]
        public async Task EndpointWithoutATypeAnswersOnEveryFeed(FeedType feedType)
        {
            await RunAsync(feedType, endpointType: null);

            Assert.True(_nextCalled);
        }

        [Fact]
        public async Task WithAccountsAnAnonymousCallerIsNotToldTheFeedsType()
        {
            // The endpoint rejects the caller itself, the same way as for a feed that doesn't exist.
            await RunAsync(FeedType.Pub, FeedType.NuGet, AuthenticationMode.Local, authenticated: false);

            Assert.True(_nextCalled);
        }

        [Fact]
        public async Task WithAccountsASignedInCallerGetsNotFound()
        {
            var context = await RunAsync(FeedType.Pub, FeedType.NuGet, AuthenticationMode.Local, authenticated: true);

            Assert.False(_nextCalled);
            Assert.Equal(404, context.Response.StatusCode);
        }
    }
}
