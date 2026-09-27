using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;
using PaGetto.Core.Upstream.Clients;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace PaGetto.Core.Tests.Upstream;

public class UpstreamClientFactoryTests
{
    public class CreateForFeed : FactsBase
    {
        [Fact]
        public void ReturnsDisabledClientWhenMirrorIsDisabled()
        {
            var feed = MirrorFeed();
            feed.Mirrors[0].Enabled = false;

            var result = Target.CreateForFeed(feed);

            Assert.IsType<DisabledUpstreamClient>(result);
        }

        [Fact]
        public void ReturnsDisabledClientWhenFeedHasNoMirrors()
        {
            var feed = MirrorFeed();
            feed.Mirrors.Clear();

            var result = Target.CreateForFeed(feed);

            Assert.IsType<DisabledUpstreamClient>(result);
        }

        [Fact]
        public void ReturnsSingleClientForOneMirror()
        {
            var result = Target.CreateForFeed(MirrorFeed());

            Assert.IsType<V3UpstreamClient>(result);
        }

        [Fact]
        public void ReturnsFallbackClientForSeveralMirrors()
        {
            var feed = MirrorFeed();
            feed.Mirrors.Add(Mirror("https://vendor.test/v3/index.json", sortOrder: 1));

            var result = Target.CreateForFeed(feed);

            Assert.IsType<FallbackUpstreamClient>(result);
        }

        [Fact]
        public void WrapsClientInListingCacheWhenEnabled()
        {
            var feed = MirrorFeed();
            feed.UpstreamListingCacheSeconds = 300;

            var result = Target.CreateForFeed(feed);

            Assert.IsType<CachingUpstreamClient>(result);
        }

        [Fact]
        public void WrapsFallbackClientInListingCacheWhenEnabled()
        {
            var feed = MirrorFeed();
            feed.UpstreamListingCacheSeconds = 300;
            feed.Mirrors.Add(Mirror("https://vendor.test/v3/index.json", sortOrder: 1));

            var result = Target.CreateForFeed(feed);

            Assert.IsType<CachingUpstreamClient>(result);
        }

        [Fact]
        public void DoesNotWrapClientWhenListingCacheIsZero()
        {
            var feed = MirrorFeed();
            feed.UpstreamListingCacheSeconds = 0;

            var result = Target.CreateForFeed(feed);

            Assert.IsType<V3UpstreamClient>(result);
        }

        [Fact]
        public void DoesNotWrapDisabledClient()
        {
            var feed = MirrorFeed();
            feed.UpstreamListingCacheSeconds = 300;
            feed.Mirrors.Clear();

            var result = Target.CreateForFeed(feed);

            Assert.IsType<DisabledUpstreamClient>(result);
        }

        [Fact]
        public void IgnoresDisabledMirrors()
        {
            var feed = MirrorFeed();
            var disabled = Mirror("https://vendor.test/v3/index.json", sortOrder: 1);
            disabled.Enabled = false;
            feed.Mirrors.Add(disabled);

            var result = Target.CreateForFeed(feed);

            Assert.IsType<V3UpstreamClient>(result);
        }

        [Fact]
        public async Task QueriesMirrorsInSortOrder()
        {
            var feed = MirrorFeed();
            feed.Mirrors[0].SortOrder = 1;
            feed.Mirrors.Add(Mirror("https://vendor.test/v3/index.json", sortOrder: 0));

            await Target.CreateForFeed(feed).DownloadPackageOrNullAsync(
                "Package", NuGet.Versioning.NuGetVersion.Parse("1.0.0"), CancellationToken.None);

            Assert.Equal(
                new[] { "vendor.test", "upstream.test" },
                Handler.Requests.ConvertAll(r => r.RequestUri.Host));
        }

        [Fact]
        public async Task SendsBasicAuthHeader()
        {
            var feed = MirrorFeed();
            feed.Mirrors[0].AuthType = MirrorAuthenticationType.Basic;
            feed.Mirrors[0].AuthUsername = "user";
            feed.Mirrors[0].AuthPassword = "password";

            await Target.CreateForFeed(feed).ListPackageVersionsAsync("Package", CancellationToken.None);

            var request = Assert.Single(Handler.Requests);
            Assert.Equal("Basic", request.Headers.Authorization.Scheme);
            Assert.Equal("dXNlcjpwYXNzd29yZA==", request.Headers.Authorization.Parameter);
        }

        [Fact]
        public async Task SendsBearerAuthHeader()
        {
            var feed = MirrorFeed();
            feed.Mirrors[0].AuthType = MirrorAuthenticationType.Bearer;
            feed.Mirrors[0].AuthToken = "token";

            await Target.CreateForFeed(feed).ListPackageVersionsAsync("Package", CancellationToken.None);

            var request = Assert.Single(Handler.Requests);
            Assert.Equal("Bearer", request.Headers.Authorization.Scheme);
            Assert.Equal("token", request.Headers.Authorization.Parameter);
        }

        [Fact]
        public async Task SendsCustomHeadersExceptBlockedOnes()
        {
            var feed = MirrorFeed();
            feed.Mirrors[0].AuthType = MirrorAuthenticationType.Custom;
            feed.Mirrors[0].AuthCustomHeaders = "{\"X-Api-Key\":\"secret\",\"Cookie\":\"evil\"}";

            await Target.CreateForFeed(feed).ListPackageVersionsAsync("Package", CancellationToken.None);

            var request = Assert.Single(Handler.Requests);
            Assert.Equal(new[] { "secret" }, request.Headers.GetValues("X-Api-Key"));
            Assert.False(request.Headers.Contains("Cookie"));
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly RecordingHandler Handler = new();
        protected readonly UpstreamListingCache ListingCache = new();
        protected readonly UpstreamClientFactory Target;

        protected FactsBase()
        {
            // The listing cache is off globally so the tests see the undecorated clients; tests
            // that cover the cache turn it on per feed.
            var options = new Mock<IOptionsSnapshot<PaGettoOptions>>();
            options.Setup(o => o.Value).Returns(new PaGettoOptions { UpstreamListingCacheSeconds = 0 });

            Target = new UpstreamClientFactory(
                new FeedSettingsResolver(options.Object),
                new DisabledUpstreamClient(),
                ListingCache,
                NullLoggerFactory.Instance,
                Handler);
        }

        public void Dispose()
        {
            ListingCache.Dispose();
            GC.SuppressFinalize(this);
        }

        protected static Feed MirrorFeed()
        {
            return new Feed
            {
                Id = Guid.NewGuid(),
                Slug = "feed",
                Mirrors = [Mirror("https://upstream.test/v3/index.json")],
            };
        }

        protected static FeedMirror Mirror(string packageSource, int sortOrder = 0)
        {
            return new FeedMirror
            {
                Enabled = true,
                PackageSource = packageSource,
                SortOrder = sortOrder,
            };
        }
    }

    public class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
