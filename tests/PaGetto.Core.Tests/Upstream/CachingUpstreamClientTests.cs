using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using PaGetto.Core.Upstream;
using PaGetto.Core.Upstream.Clients;
using Microsoft.Extensions.Internal;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace PaGetto.Core.Tests.Upstream;

public class CachingUpstreamClientTests
{
    public class ListPackageVersionsAsync : FactsBase
    {
        [Fact]
        public async Task SecondRequestWithinTtlDoesNotCallUpstream()
        {
            Inner.Setup(u => u.ListPackageVersionsAsync(Id, Cancellation)).ReturnsAsync(Versions("1.0.0"));

            await Target.ListPackageVersionsAsync(Id, Cancellation);
            Clock.Advance(Ttl - TimeSpan.FromSeconds(1));
            var result = await Target.ListPackageVersionsAsync(Id, Cancellation);

            Assert.Equal(Versions("1.0.0"), result);
            Inner.Verify(u => u.ListPackageVersionsAsync(Id, Cancellation), Times.Once);
        }

        [Fact]
        public async Task RequestAfterTtlCallsUpstreamAgain()
        {
            Inner.SetupSequence(u => u.ListPackageVersionsAsync(Id, Cancellation))
                .ReturnsAsync(Versions("1.0.0"))
                .ReturnsAsync(Versions("1.0.0", "2.0.0"));

            await Target.ListPackageVersionsAsync(Id, Cancellation);
            Clock.Advance(Ttl + TimeSpan.FromSeconds(1));
            var result = await Target.ListPackageVersionsAsync(Id, Cancellation);

            Assert.Equal(Versions("1.0.0", "2.0.0"), result);
            Inner.Verify(u => u.ListPackageVersionsAsync(Id, Cancellation), Times.Exactly(2));
        }

        [Fact]
        public async Task PackageIdIsCaseInsensitive()
        {
            Inner.Setup(u => u.ListPackageVersionsAsync(It.IsAny<string>(), Cancellation)).ReturnsAsync(Versions("1.0.0"));

            await Target.ListPackageVersionsAsync("Package", Cancellation);
            await Target.ListPackageVersionsAsync("PACKAGE", Cancellation);

            Inner.Verify(u => u.ListPackageVersionsAsync(It.IsAny<string>(), Cancellation), Times.Once);
        }

        [Fact]
        public async Task DoesNotCacheEmptyResults()
        {
            Inner.Setup(u => u.ListPackageVersionsAsync(Id, Cancellation)).ReturnsAsync(Versions());

            await Target.ListPackageVersionsAsync(Id, Cancellation);
            await Target.ListPackageVersionsAsync(Id, Cancellation);

            Inner.Verify(u => u.ListPackageVersionsAsync(Id, Cancellation), Times.Exactly(2));
        }

        [Fact]
        public async Task ChangedFeedSettingsInvalidateTheCache()
        {
            Inner.Setup(u => u.ListPackageVersionsAsync(Id, Cancellation)).ReturnsAsync(Versions("1.0.0"));

            await Target.ListPackageVersionsAsync(Id, Cancellation);
            Feed.UpdatedAtUtc = Feed.UpdatedAtUtc.AddSeconds(1);
            await CreateTarget().ListPackageVersionsAsync(Id, Cancellation);

            Inner.Verify(u => u.ListPackageVersionsAsync(Id, Cancellation), Times.Exactly(2));
        }

        [Fact]
        public async Task FeedsDoNotShareEntries()
        {
            Inner.Setup(u => u.ListPackageVersionsAsync(Id, Cancellation)).ReturnsAsync(Versions("1.0.0"));

            await Target.ListPackageVersionsAsync(Id, Cancellation);
            Feed.Id = Guid.NewGuid();
            await CreateTarget().ListPackageVersionsAsync(Id, Cancellation);

            Inner.Verify(u => u.ListPackageVersionsAsync(Id, Cancellation), Times.Exactly(2));
        }
    }

    public class ListPackagesAsync : FactsBase
    {
        [Fact]
        public async Task SecondRequestWithinTtlDoesNotCallUpstream()
        {
            Inner.Setup(u => u.ListPackagesAsync(Id, Cancellation)).ReturnsAsync(Packages("1.0.0"));

            await Target.ListPackagesAsync(Id, Cancellation);
            var result = await Target.ListPackagesAsync(Id, Cancellation);

            Assert.Equal("1.0.0", Assert.Single(result).Version.ToNormalizedString());
            Inner.Verify(u => u.ListPackagesAsync(Id, Cancellation), Times.Once);
        }

        [Fact]
        public async Task RequestAfterTtlCallsUpstreamAgain()
        {
            Inner.Setup(u => u.ListPackagesAsync(Id, Cancellation)).ReturnsAsync(Packages("1.0.0"));

            await Target.ListPackagesAsync(Id, Cancellation);
            Clock.Advance(Ttl + TimeSpan.FromSeconds(1));
            await Target.ListPackagesAsync(Id, Cancellation);

            Inner.Verify(u => u.ListPackagesAsync(Id, Cancellation), Times.Exactly(2));
        }

        [Fact]
        public async Task DoesNotShareEntriesWithVersionListings()
        {
            Inner.Setup(u => u.ListPackageVersionsAsync(Id, Cancellation)).ReturnsAsync(Versions("1.0.0"));
            Inner.Setup(u => u.ListPackagesAsync(Id, Cancellation)).ReturnsAsync(Packages("1.0.0"));

            await Target.ListPackageVersionsAsync(Id, Cancellation);
            await Target.ListPackagesAsync(Id, Cancellation);

            Inner.Verify(u => u.ListPackagesAsync(Id, Cancellation), Times.Once);
        }

        [Fact]
        public async Task DoesNotCacheEmptyResults()
        {
            Inner.Setup(u => u.ListPackagesAsync(Id, Cancellation)).ReturnsAsync(new List<Package>());

            await Target.ListPackagesAsync(Id, Cancellation);
            await Target.ListPackagesAsync(Id, Cancellation);

            Inner.Verify(u => u.ListPackagesAsync(Id, Cancellation), Times.Exactly(2));
        }
    }

    public class DownloadPackageOrNullAsync : FactsBase
    {
        [Fact]
        public async Task IsNotCached()
        {
            var version = NuGetVersion.Parse("1.0.0");
            Inner.Setup(u => u.DownloadPackageOrNullAsync(Id, version, Cancellation))
                .ReturnsAsync(() => new MemoryStream());

            await Target.DownloadPackageOrNullAsync(Id, version, Cancellation);
            await Target.DownloadPackageOrNullAsync(Id, version, Cancellation);

            Inner.Verify(u => u.DownloadPackageOrNullAsync(Id, version, Cancellation), Times.Exactly(2));
        }
    }

    public class GetServiceIndexUrl : FactsBase
    {
        [Fact]
        public void ReturnsInnerUrl()
        {
            Inner.Setup(u => u.GetServiceIndexUrl()).Returns("https://upstream.test/v3/index.json");

            Assert.Equal("https://upstream.test/v3/index.json", Target.GetServiceIndexUrl());
        }
    }

    public class FactsBase : IDisposable
    {
        protected const string Id = "Package";
        protected static readonly TimeSpan Ttl = TimeSpan.FromSeconds(300);
        protected readonly CancellationToken Cancellation = CancellationToken.None;

        protected readonly Mock<IUpstreamClient> Inner = new();
        protected readonly FakeClock Clock = new();
        protected readonly UpstreamListingCache Cache;
        protected readonly Feed Feed = new()
        {
            Id = Guid.NewGuid(),
            Slug = "feed",
            UpdatedAtUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        protected FactsBase()
        {
            Cache = new UpstreamListingCache(Clock);
            Target = CreateTarget();
        }

        protected CachingUpstreamClient Target { get; }

        protected CachingUpstreamClient CreateTarget()
        {
            return new CachingUpstreamClient(Inner.Object, Cache, Feed, Ttl);
        }

        protected static IReadOnlyList<NuGetVersion> Versions(params string[] versions)
        {
            return versions.Select(NuGetVersion.Parse).ToList();
        }

        protected static IReadOnlyList<Package> Packages(params string[] versions)
        {
            return versions.Select(v => new Package { Id = Id, Version = NuGetVersion.Parse(v) }).ToList();
        }

        public void Dispose()
        {
            Cache.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    public class FakeClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan duration)
        {
            UtcNow += duration;
        }
    }
}
