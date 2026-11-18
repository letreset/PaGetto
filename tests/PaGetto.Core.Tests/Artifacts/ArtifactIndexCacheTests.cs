using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using Xunit;

namespace PaGetto.Core.Tests.Artifacts;

public class ArtifactIndexCacheTests
{
    public class GetOrBuildAsync : FactsBase
    {
        [Fact]
        public async Task KeepsTheIndexWhileTheFeedIsUnchanged()
        {
            Assert.Equal("1", await GetAsync(Feed, "versions"));
            Assert.Equal("1", await GetAsync(Feed, "versions"));

            Assert.Equal(1, Builds);
        }

        [Fact]
        public async Task BuildsAgainAfterAPackageOfTheFeedChanged()
        {
            await GetAsync(Feed, "versions");

            Feed.ArtifactRevision++;

            Assert.Equal("2", await GetAsync(Feed, "versions"));
        }

        [Fact]
        public async Task BuildsAgainAfterTheFeedsSettingsChanged()
        {
            await GetAsync(Feed, "versions");

            Feed.UpdatedAtUtc = Feed.UpdatedAtUtc.AddMinutes(1);

            Assert.Equal("2", await GetAsync(Feed, "versions"));
        }

        [Fact]
        public async Task KeepsIndexesAndFeedsApart()
        {
            var other = new Feed { Id = Guid.NewGuid(), UpdatedAtUtc = Feed.UpdatedAtUtc };

            Assert.Equal("1", await GetAsync(Feed, "versions"));
            Assert.Equal("2", await GetAsync(Feed, "names"));
            Assert.Equal("3", await GetAsync(other, "versions"));
            Assert.Equal("1", await GetAsync(Feed, "versions"));
        }

        [Fact]
        public async Task BuildsEveryTimeWithoutADuration()
        {
            await Target.GetOrBuildAsync(Feed, "versions", TimeSpan.Zero, BuildAsync);
            await Target.GetOrBuildAsync(Feed, "versions", TimeSpan.Zero, BuildAsync);

            Assert.Equal(2, Builds);
        }

        [Fact]
        public async Task DoesNotCacheAFailedBuild()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Target.GetOrBuildAsync(Feed, "versions", TimeSpan.FromMinutes(1), () => throw new InvalidOperationException()));

            Assert.Equal("1", await GetAsync(Feed, "versions"));
        }
    }

    public class GetMirroredIndexDuration
    {
        [Fact]
        public void IsTheLocalDurationForAFeedWithoutMirrors()
        {
            var feed = new Feed { UpstreamListingCacheSeconds = 60 };

            Assert.Equal(ArtifactIndexCache.LocalIndexDuration, ArtifactIndexCache.GetMirroredIndexDuration(Settings(), feed));
        }

        [Fact]
        public void IsTheUpstreamListingDurationForAFeedWithMirrors()
        {
            var feed = new Feed
            {
                UpstreamListingCacheSeconds = 60,
                Mirrors = [new FeedMirror { PackageSource = "https://upstream.test/", Enabled = true }],
            };

            Assert.Equal(TimeSpan.FromSeconds(60), ArtifactIndexCache.GetMirroredIndexDuration(Settings(), feed));
        }

        [Fact]
        public void IsZeroForAFeedWithMirrorsWhoseListingsAreNotCached()
        {
            var feed = new Feed
            {
                UpstreamListingCacheSeconds = 0,
                Mirrors = [new FeedMirror { PackageSource = "https://upstream.test/", Enabled = true }],
            };

            Assert.Equal(TimeSpan.Zero, ArtifactIndexCache.GetMirroredIndexDuration(Settings(), feed));
        }

        private static FeedSettingsResolver Settings()
        {
            var options = new Mock<IOptionsSnapshot<PaGettoOptions>>();
            options.Setup(o => o.Value).Returns(new PaGettoOptions());

            return new FeedSettingsResolver(options.Object);
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly ArtifactIndexCache Target = new();
        protected readonly Feed Feed = new() { Id = Guid.NewGuid(), UpdatedAtUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        protected int Builds;

        protected async Task<string> GetAsync(Feed feed, string index)
        {
            return Encoding.UTF8.GetString(await Target.GetOrBuildAsync(feed, index, TimeSpan.FromMinutes(1), BuildAsync));
        }

        // Each build returns the number of builds so far, so a test sees whether it was cached.
        protected Task<byte[]> BuildAsync()
        {
            Builds++;
            return Task.FromResult(Encoding.UTF8.GetBytes(Builds.ToString(CultureInfo.InvariantCulture)));
        }

        public void Dispose()
        {
            Target.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
