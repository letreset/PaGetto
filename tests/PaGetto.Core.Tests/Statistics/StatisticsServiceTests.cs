using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using PaGetto.Core.Statistics;
using PaGetto.Core.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;

namespace PaGetto.Core.Tests.Statistics;

public class StatisticsServiceTests
{
    public class GetFeedStatisticsAsync : FactsBase
    {
        [Fact]
        public async Task SumsDownloadsAndSizesAndCountsVersionsOfTheFeedOnly()
        {
            AddPackage("A", "1.0.0", downloads: 5, size: 100);
            AddPackage("A", "2.0.0-beta", downloads: 3, size: 200, prerelease: true);
            AddPackage("B", "1.0.0", downloads: 1, size: null, listed: false);
            AddPackage("C", "1.0.0", downloads: 50, size: 1000, feedId: OtherFeedId);
            Context.SaveChanges();

            var stats = await Target.GetFeedStatisticsAsync(FeedId, 10, Ct);

            Assert.Equal(9, stats.TotalDownloads);
            Assert.Equal(300, stats.TotalSizeBytes);
            Assert.Equal(2, stats.StableVersions);
            Assert.Equal(1, stats.PrereleaseVersions);
            Assert.Equal(1, stats.UnlistedVersions);
        }

        [Fact]
        public async Task ReturnsZerosAndEmptyListsForAnEmptyFeed()
        {
            var stats = await Target.GetFeedStatisticsAsync(FeedId, 10, Ct);

            Assert.Equal(0, stats.TotalDownloads);
            Assert.Equal(0, stats.TotalSizeBytes);
            Assert.Equal(0, stats.StableVersions);
            Assert.Empty(stats.MostDownloaded);
            Assert.Empty(stats.RecentlyPublished);
        }

        [Fact]
        public async Task ListsTheMostDownloadedPackagesWithTheDownloadsOfAllVersions()
        {
            AddPackage("A", "1.0.0", downloads: 5);
            AddPackage("A", "2.0.0", downloads: 6);
            AddPackage("B", "1.0.0", downloads: 20);
            AddPackage("C", "1.0.0", downloads: 1);
            AddPackage("D", "1.0.0", downloads: 0);
            Context.SaveChanges();

            var stats = await Target.GetFeedStatisticsAsync(FeedId, 2, Ct);

            Assert.Equal(
                [("B", 20L), ("A", 11L)],
                stats.MostDownloaded.Select(p => (p.Id, p.Downloads)));
        }

        [Fact]
        public async Task SkipsPackagesWithoutDownloads()
        {
            AddPackage("A", "1.0.0", downloads: 0);
            Context.SaveChanges();

            var stats = await Target.GetFeedStatisticsAsync(FeedId, 10, Ct);

            Assert.Empty(stats.MostDownloaded);
        }

        [Fact]
        public async Task ListsTheNewestListedVersionsFirst()
        {
            AddPackage("A", "1.0.0", published: new DateTime(2024, 1, 1));
            AddPackage("A", "2.0.0", published: new DateTime(2024, 3, 1));
            AddPackage("B", "1.0.0", published: new DateTime(2024, 2, 1));
            AddPackage("B", "2.0.0", published: new DateTime(2024, 4, 1), listed: false);
            Context.SaveChanges();

            var stats = await Target.GetFeedStatisticsAsync(FeedId, 2, Ct);

            Assert.Equal(
                [("A", "2.0.0"), ("B", "1.0.0")],
                stats.RecentlyPublished.Select(p => (p.Id, p.Version)));
        }
    }

    public class FactsBase : IDisposable
    {
        protected static readonly Guid FeedId = Guid.NewGuid();
        protected static readonly Guid OtherFeedId = Guid.NewGuid();

        protected readonly TestDbContext Context;
        protected readonly StatisticsService Target;
        protected readonly CancellationToken Ct = CancellationToken.None;

        private readonly ServiceProvider _provider;

        protected FactsBase()
        {
            Context = TestDbContext.Create();
            Context.Feeds.Add(new Feed { Id = FeedId, Slug = "feed", Name = "Feed" });
            Context.Feeds.Add(new Feed { Id = OtherFeedId, Slug = "other", Name = "Other" });
            Context.SaveChanges();

            _provider = new ServiceCollection()
                .AddSingleton<IContext>(Context)
                .BuildServiceProvider();
            Target = new StatisticsService(_provider);
        }

        protected void AddPackage(
            string id,
            string version,
            long downloads = 0,
            long? size = null,
            bool prerelease = false,
            bool listed = true,
            DateTime? published = null,
            Guid? feedId = null)
        {
            Context.Packages.Add(new Package
            {
                Id = id,
                Version = NuGetVersion.Parse(version),
                FeedId = feedId ?? FeedId,
                Downloads = downloads,
                Size = size,
                IsPrerelease = prerelease,
                Listed = listed,
                Published = published ?? new DateTime(2024, 1, 1),
                SemVerLevel = SemVerLevel.Unknown,
                Authors = [],
                Tags = [],
            });
        }

        public void Dispose()
        {
            _provider.Dispose();
            Context.Dispose();
        }
    }
}
