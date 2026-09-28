using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace PaGetto.Web.Tests.Middleware;

public class FeedResolutionMiddlewareFacts
{
    public class InvokeAsync
    {
        private readonly Mock<IFeedService> _feeds = new();
        private readonly FeedContext _feedContext = new();
        private bool _nextCalled;

        private async Task<HttpContext> RunAsync(string path)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            var middleware = new FeedResolutionMiddleware(_ =>
            {
                _nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context, _feeds.Object, _feedContext);
            return context;
        }

        [Theory]
        [InlineData("/feeds")]
        [InlineData("/feeds/")]
        public async Task PathWithoutSlugReturns404(string path)
        {
            var context = await RunAsync(path);

            Assert.Equal(404, context.Response.StatusCode);
            Assert.False(_nextCalled);
            _feeds.Verify(f => f.GetFeedBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task KnownSlugMovesTheSlugIntoPathBase()
        {
            var feed = new Feed { Slug = "dev" };
            _feeds.Setup(f => f.GetFeedBySlugAsync("dev", It.IsAny<CancellationToken>())).ReturnsAsync(feed);

            var context = await RunAsync("/feeds/dev/v3/index.json");

            Assert.True(_nextCalled);
            Assert.Equal("/feeds/dev", context.Request.PathBase);
            Assert.Equal("/v3/index.json", context.Request.Path);
            Assert.Same(feed, _feedContext.CurrentFeed);
            Assert.False(_feedContext.IsDefaultRoute);
        }

        [Fact]
        public async Task UnknownSlugForSignedInCallerReturns404()
        {
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "bob")], "Test")),
            };
            context.Request.Path = "/feeds/nope/v3/search";

            await new FeedResolutionMiddleware(_ =>
            {
                _nextCalled = true;
                return Task.CompletedTask;
            }).InvokeAsync(context, _feeds.Object, _feedContext);

            Assert.Equal(404, context.Response.StatusCode);
            Assert.False(_nextCalled);
        }

        [Fact]
        public async Task UnknownSlugForAnonymousCallerContinuesWithStandInFeed()
        {
            var context = await RunAsync("/feeds/nope/v3/search");

            Assert.True(_nextCalled);
            Assert.Equal("/feeds/nope", context.Request.PathBase);
            Assert.Equal("/v3/search", context.Request.Path);
            Assert.Equal("nope", _feedContext.CurrentFeed.Slug);
            Assert.NotEqual(Feed.DefaultId, _feedContext.CurrentFeed.Id);
        }

        [Fact]
        public async Task OtherPathsUseTheDefaultFeed()
        {
            var feed = new Feed { Slug = Feed.DefaultSlug };
            _feeds.Setup(f => f.GetDefaultFeedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(feed);

            await RunAsync("/v3/index.json");

            Assert.True(_nextCalled);
            Assert.Same(feed, _feedContext.CurrentFeed);
            Assert.True(_feedContext.IsDefaultRoute);
        }
    }
}
