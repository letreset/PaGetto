using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Indexing;
using PaGetto.Core.Upstream;
using Microsoft.Extensions.Logging;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace PaGetto.Core.Tests.Services;

public class PackageServiceTests
{
    private static readonly Guid _feedId = Guid.Empty;
    private const string FeedSlug = "default";

    public class FindPackageVersionsAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsEmpty()
        {
            Setup();

            var results = await Target.FindPackageVersionsAsync(
                _feedId,
                "MyPackage",
                CancellationToken);

            Assert.Empty(results);
        }

        [Fact]
        public async Task ReturnsLocalVersions()
        {
            Setup(localPackages: new List<Package>
            {
                new Package { Version = new NuGetVersion("1.0.0") },
                new Package { Version = new NuGetVersion("2.0.0") },
            });

            var results = await Target.FindPackageVersionsAsync(
                _feedId,
                "MyPackage",
                CancellationToken);

            Assert.Equal(2, results.Count);
            Assert.Equal("1.0.0", results[0].OriginalVersion);
            Assert.Equal("2.0.0", results[1].OriginalVersion);
        }

        [Fact]
        public async Task ReturnsUpstreamVersions()
        {
            Setup(upstreamPackages: new List<NuGetVersion>
            {
                new NuGetVersion("1.0.0"),
                new NuGetVersion("2.0.0"),
            });

            var results = await Target.FindPackageVersionsAsync(
                _feedId,
                "MyPackage",
                CancellationToken);

            Assert.Equal(2, results.Count);
            Assert.Equal("1.0.0", results[0].OriginalVersion);
            Assert.Equal("2.0.0", results[1].OriginalVersion);
        }

        [Fact]
        public async Task MergesLocalAndUpstreamVersions()
        {
            Setup(
                localPackages: new List<Package>
                {
                    new Package { Version = new NuGetVersion("1.0.0") },
                    new Package { Version = new NuGetVersion("2.0.0") },
                },
                upstreamPackages: new List<NuGetVersion>
                {
                    new NuGetVersion("2.0.0"),
                    new NuGetVersion("3.0.0"),
                });

            var results = await Target.FindPackageVersionsAsync(
                _feedId,
                "MyPackage",
                CancellationToken);

            var ordered = results.OrderBy(v => v).ToList();

            Assert.Equal(3, ordered.Count);
            Assert.Equal("1.0.0", ordered[0].OriginalVersion);
            Assert.Equal("2.0.0", ordered[1].OriginalVersion);
            Assert.Equal("3.0.0", ordered[2].OriginalVersion);
        }

        private void Setup(
            IReadOnlyList<Package> localPackages = null,
            IReadOnlyList<NuGetVersion> upstreamPackages = null)
        {
            localPackages ??= new List<Package>();
            upstreamPackages ??= new List<NuGetVersion>();

            DB
                .Setup(p => p.FindAsync(
                    It.IsAny<Guid>(),
                    "MyPackage",
                    /*includeUnlisted: */ true,
                    CancellationToken))
                .ReturnsAsync(localPackages);

            Upstream
                .Setup(u => u.ListPackageVersionsAsync(
                    "MyPackage",
                    CancellationToken))
                .ReturnsAsync(upstreamPackages);
        }
    }

    public class FindPackagesAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsEmpty()
        {
            Setup();

            var results = await Target.FindPackagesAsync(_feedId, "MyPackage", CancellationToken);

            Assert.Empty(results);
        }

        [Fact]
        public async Task ReturnsLocalPackages()
        {
            Setup(localPackages: new List<Package>
            {
                new Package { Version = new NuGetVersion("1.0.0") },
                new Package { Version = new NuGetVersion("2.0.0") },
            });

            var results = await Target.FindPackagesAsync(_feedId, "MyPackage", CancellationToken);

            Assert.Equal(2, results.Count);
            Assert.Equal("1.0.0", results[0].Version.OriginalVersion);
            Assert.Equal("2.0.0", results[1].Version.OriginalVersion);
        }

        [Fact]
        public async Task ReturnsUpstreamPackages()
        {
            Setup(upstreamPackages: new List<Package>
            {
                new Package { Version = new NuGetVersion("1.0.0") },
                new Package { Version = new NuGetVersion("2.0.0") },
            });

            var results = await Target.FindPackagesAsync(_feedId, "MyPackage", CancellationToken);

            Assert.Equal(2, results.Count);
            Assert.Equal("1.0.0", results[0].Version.OriginalVersion);
            Assert.Equal("2.0.0", results[1].Version.OriginalVersion);
        }

        [Fact]
        public async Task MergesLocalAndUpstreamPackages()
        {
            Setup(
                localPackages: new List<Package>
                {
                    new Package { Version = new NuGetVersion("1.0.0") },
                    new Package { Version = new NuGetVersion("2.0.0") },
                },
                upstreamPackages: new List<Package>
                {
                    new Package { Version = new NuGetVersion("2.0.0") },
                    new Package { Version = new NuGetVersion("3.0.0") },
                });

            var results = await Target.FindPackagesAsync(_feedId, "MyPackage", CancellationToken);
            var ordered = results.OrderBy(p => p.Version).ToList();

            Assert.Equal(3, ordered.Count);
            Assert.Equal("1.0.0", ordered[0].Version.OriginalVersion);
            Assert.Equal("2.0.0", ordered[1].Version.OriginalVersion);
            Assert.Equal("3.0.0", ordered[2].Version.OriginalVersion);
        }

        private void Setup(
            IReadOnlyList<Package> localPackages = null,
            IReadOnlyList<Package> upstreamPackages = null)
        {
            localPackages ??= new List<Package>();
            upstreamPackages ??= new List<Package>();

            DB
                .Setup(p => p.FindAsync(
                    It.IsAny<Guid>(),
                    "MyPackage",
                    /*includeUnlisted: */ true,
                    CancellationToken))
                .ReturnsAsync(localPackages);

            Upstream
                .Setup(u => u.ListPackagesAsync(
                    "MyPackage",
                    CancellationToken))
                .ReturnsAsync(upstreamPackages);
        }
    }

    public class FindPackageOrNullAsync : MirrorAsync
    {
        protected override async Task TargetAsync()
            => await Target.FindPackageOrNullAsync(_feedId, FeedSlug, ID, Version, CancellationToken);

        [Fact]
        public async Task ExistsInDatabase()
        {
            var expected = new Package();

            DB
                .Setup(p => p.ExistsAsync(It.IsAny<Guid>(), ID, Version, CancellationToken))
                .ReturnsAsync(true);
            DB
                .Setup(p => p.FindOrNullAsync(It.IsAny<Guid>(), ID, Version, /*includeUnlisted:*/ true, CancellationToken))
                .ReturnsAsync(expected);

            var result = await Target.FindPackageOrNullAsync(_feedId, FeedSlug, ID, Version, CancellationToken);

            Assert.Same(expected, result);
        }

        [Fact]
        public async Task DoesNotExistInDatabase()
        {
            DB
                .Setup(p => p.FindOrNullAsync(It.IsAny<Guid>(), ID, Version, /*includeUnlisted:*/ true, CancellationToken))
                .ReturnsAsync((Package)null);

            var result = await Target.FindPackageOrNullAsync(_feedId, FeedSlug, ID, Version, CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsPackageWhenAlreadyIndexedConcurrently()
        {
            var expected = new Package();

            DB
                .Setup(p => p.ExistsAsync(It.IsAny<Guid>(), ID, Version, CancellationToken))
                .ReturnsAsync(false);
            Upstream
                .Setup(u => u.DownloadPackageOrNullAsync(ID, Version, CancellationToken))
                .ReturnsAsync(new MemoryStream());
            Indexer
                .Setup(i => i.IndexAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<DateTime?>(), CancellationToken))
                .ReturnsAsync(PackageIndexingResult.PackageAlreadyExists);
            DB
                .Setup(p => p.FindOrNullAsync(It.IsAny<Guid>(), ID, Version, /*includeUnlisted:*/ true, CancellationToken))
                .ReturnsAsync(expected);

            var result = await Target.FindPackageOrNullAsync(_feedId, FeedSlug, ID, Version, CancellationToken);

            Assert.Same(expected, result);
        }
    }

    public class ExistsAsync : MirrorAsync
    {
        protected override async Task TargetAsync() => await Target.ExistsAsync(_feedId, FeedSlug, ID, Version, CancellationToken);

        [Fact]
        public async Task ExistsInDatabase()
        {
            DB
                .Setup(p => p.ExistsAsync(It.IsAny<Guid>(), ID, Version, CancellationToken))
                .ReturnsAsync(true);

            var result = await Target.ExistsAsync(_feedId, FeedSlug, ID, Version, CancellationToken);

            Assert.True(result);
        }

        [Fact]
        public async Task DoesNotExistInDatabase()
        {
            DB
                .Setup(p => p.ExistsAsync(It.IsAny<Guid>(), ID, Version, CancellationToken))
                .ReturnsAsync(false);

            var result = await Target.ExistsAsync(_feedId, FeedSlug, ID, Version, CancellationToken);

            Assert.False(result);
        }

        [Fact]
        public async Task ReturnsTrueWhenAlreadyIndexedConcurrently()
        {
            DB
                .Setup(p => p.ExistsAsync(It.IsAny<Guid>(), ID, Version, CancellationToken))
                .ReturnsAsync(false);
            Upstream
                .Setup(u => u.DownloadPackageOrNullAsync(ID, Version, CancellationToken))
                .ReturnsAsync(new MemoryStream());
            Indexer
                .Setup(i => i.IndexAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<DateTime?>(), CancellationToken))
                .ReturnsAsync(PackageIndexingResult.PackageAlreadyExists);

            var result = await Target.ExistsAsync(_feedId, FeedSlug, ID, Version, CancellationToken);

            Assert.True(result);
        }

        [Fact]
        public async Task ConcurrentRequestsForColdPackageAllSucceedAndIndexOnce()
        {
            var id = "ConcurrentPackage";
            var indexed = 0;

            DB
                .Setup(p => p.ExistsAsync(It.IsAny<Guid>(), id, Version, CancellationToken))
                .ReturnsAsync(() => Volatile.Read(ref indexed) == 1);
            Upstream
                .Setup(u => u.DownloadPackageOrNullAsync(id, Version, CancellationToken))
                .Returns(async () =>
                {
                    // Widen the race window so every request reaches the download step at once.
                    await Task.Delay(50);
                    return (Stream)new MemoryStream();
                });
            Indexer
                .Setup(i => i.IndexAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<DateTime?>(), CancellationToken))
                .Returns(async () =>
                {
                    await Task.Delay(10);
                    if (Interlocked.Exchange(ref indexed, 1) == 1)
                    {
                        return PackageIndexingResult.PackageAlreadyExists;
                    }

                    return PackageIndexingResult.Success;
                });

            var results = await Task.WhenAll(
                Enumerable
                    .Range(0, 32)
                    .Select(_ => Task.Run(() => Target.ExistsAsync(_feedId, FeedSlug, id, Version, CancellationToken))));

            Assert.All(results, Assert.True);
            Indexer.Verify(
                i => i.IndexAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<DateTime?>(), CancellationToken),
                Times.Once);
        }
    }

    public abstract class MirrorAsync : FactsBase
    {
        protected readonly string ID = "MyPackage";
        protected readonly NuGetVersion Version = new NuGetVersion("1.0.0");

        protected abstract Task TargetAsync();

        [Fact]
        public async Task SkipsIfAlreadyMirrored()
        {
            DB
                .Setup(p => p.ExistsAsync(It.IsAny<Guid>(), ID, Version, CancellationToken))
                .ReturnsAsync(true);

            await TargetAsync();

            Indexer.Verify(
                i => i.IndexAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<DateTime?>(), CancellationToken),
                Times.Never);
        }

        [Fact]
        public async Task SkipsIfUpstreamDoesntHavePackage()
        {
            DB
                .Setup(p => p.ExistsAsync(It.IsAny<Guid>(), ID, Version, CancellationToken))
                .ReturnsAsync(false);

            Upstream
                .Setup(u => u.DownloadPackageOrNullAsync(ID, Version, CancellationToken))
                .ReturnsAsync((Stream)null);

            await TargetAsync();

            Indexer.Verify(
                i => i.IndexAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<DateTime?>(), CancellationToken),
                Times.Never);
        }

        [Fact]
        public async Task SkipsIfUpstreamThrows()
        {
            DB
                .Setup(p => p.ExistsAsync(It.IsAny<Guid>(), ID, Version, CancellationToken))
                .ReturnsAsync(false);

            Upstream
                .Setup(u => u.DownloadPackageOrNullAsync(ID, Version, CancellationToken))
                .ThrowsAsync(new InvalidOperationException("Hello world"));

            await TargetAsync();

            Indexer.Verify(
                i => i.IndexAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<DateTime?>(), CancellationToken),
                Times.Never);
        }

        [Fact]
        public async Task MirrorsPackage()
        {
            DB
                .Setup(p => p.ExistsAsync(It.IsAny<Guid>(), ID, Version, CancellationToken))
                .ReturnsAsync(false);

            using var downloadStream = new MemoryStream();
            Upstream
                .Setup(u => u.DownloadPackageOrNullAsync(ID, Version, CancellationToken))
                .ReturnsAsync(downloadStream);

            await TargetAsync();

            Indexer.Verify(
                i => i.IndexAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<DateTime?>(), CancellationToken),
                Times.Once);
        }

        [Theory]
        [InlineData(2024, 2024)]
        [InlineData(1900, null)]
        public async Task KeepsTheUpstreamPublishDate(int upstreamYear, int? expectedYear)
        {
            DB
                .Setup(p => p.ExistsAsync(It.IsAny<Guid>(), ID, Version, CancellationToken))
                .ReturnsAsync(false);
            Upstream
                .Setup(u => u.DownloadPackageOrNullAsync(ID, Version, CancellationToken))
                .ReturnsAsync(new MemoryStream());
            Upstream
                .Setup(u => u.ListPackagesAsync(ID, CancellationToken))
                .ReturnsAsync(new List<Package>
                {
                    new() { Id = ID, Version = Version, Published = new DateTime(upstreamYear, 5, 1, 0, 0, 0, DateTimeKind.Utc) },
                    new() { Id = ID, Version = new NuGetVersion("2.0.0"), Published = new DateTime(2025, 1, 1) },
                });

            await TargetAsync();

            var expected = expectedYear.HasValue ? new DateTime(expectedYear.Value, 5, 1, 0, 0, 0, DateTimeKind.Utc) : (DateTime?)null;
            Indexer.Verify(
                i => i.IndexAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), expected, CancellationToken),
                Times.Once);
        }
    }

    public class AddDownloadAsync : FactsBase
    {
        [Fact]
        public async Task AddsDownload()
        {
            var id = "Hello";
            var version = new NuGetVersion("1.2.3");

            await Target.AddDownloadAsync(_feedId, id, version, CancellationToken);

            DB.Verify(
                db => db.AddDownloadAsync(It.IsAny<Guid>(), id, version, CancellationToken),
                Times.Once);
        }
    }

    public class FactsBase
    {
        protected readonly Mock<IPackageDatabase> DB;
        protected readonly Mock<IUpstreamClient> Upstream;
        protected readonly Mock<IPackageIndexingService> Indexer;

        protected readonly CancellationToken CancellationToken = CancellationToken.None;
        protected readonly PackageService Target;

        protected FactsBase()
        {
            DB = new Mock<IPackageDatabase>();
            Upstream = new Mock<IUpstreamClient>();
            // Like the real clients, which return an empty list when the upstream has nothing.
            Upstream
                .Setup(u => u.ListPackagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Package>());
            Indexer = new Mock<IPackageIndexingService>();

            var defaultFeed = new Feed { Id = Guid.Empty, Slug = Feed.DefaultSlug };
            var feedService = new Mock<IFeedService>();
            feedService
                .Setup(s => s.GetFeedByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(defaultFeed);

            var upstreamFactory = new Mock<IUpstreamClientFactory>();
            upstreamFactory.Setup(f => f.CreateForFeed(It.IsAny<Feed>())).Returns(Upstream.Object);

            Target = new PackageService(
                DB.Object,
                upstreamFactory.Object,
                feedService.Object,
                Indexer.Object,
                new PackageMirrorLock(),
                Mock.Of<ILogger<PackageService>>());
        }
    }
}
