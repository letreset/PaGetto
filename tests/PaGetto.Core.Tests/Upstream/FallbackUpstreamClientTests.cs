using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using PaGetto.Core.Upstream;
using PaGetto.Core.Upstream.Clients;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace PaGetto.Core.Tests.Upstream;

public class FallbackUpstreamClientTests
{
    [Fact]
    public void Ctor_UpstreamsIsNull_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(
            () => new FallbackUpstreamClient(null, NullLogger<FallbackUpstreamClient>.Instance));
    }

    [Fact]
    public void Ctor_UpstreamsIsEmpty_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(
            () => new FallbackUpstreamClient([], NullLogger<FallbackUpstreamClient>.Instance));
    }

    public class ListPackageVersionsAsync : FactsBase
    {
        [Fact]
        public async Task UnionsVersionsInUpstreamOrder()
        {
            First.Setup(u => u.ListPackageVersionsAsync(Id, Cancellation))
                .ReturnsAsync(Versions("1.0.0", "2.0.0"));
            Second.Setup(u => u.ListPackageVersionsAsync(Id, Cancellation))
                .ReturnsAsync(Versions("2.0.0", "3.0.0"));

            var result = await Target.ListPackageVersionsAsync(Id, Cancellation);

            Assert.Equal(Versions("1.0.0", "2.0.0", "3.0.0"), result);
        }

        [Fact]
        public async Task SkipsFailingUpstream()
        {
            First.Setup(u => u.ListPackageVersionsAsync(Id, Cancellation))
                .ThrowsAsync(new HttpRequestException("Upstream is down"));
            Second.Setup(u => u.ListPackageVersionsAsync(Id, Cancellation))
                .ReturnsAsync(Versions("3.0.0"));

            var result = await Target.ListPackageVersionsAsync(Id, Cancellation);

            Assert.Equal(Versions("3.0.0"), result);
        }

        [Fact]
        public async Task ReturnsEmptyWhenNoUpstreamHasPackage()
        {
            First.Setup(u => u.ListPackageVersionsAsync(Id, Cancellation)).ReturnsAsync(Versions());
            Second.Setup(u => u.ListPackageVersionsAsync(Id, Cancellation)).ReturnsAsync(Versions());

            var result = await Target.ListPackageVersionsAsync(Id, Cancellation);

            Assert.Empty(result);
        }
    }

    public class ListPackagesAsync : FactsBase
    {
        [Fact]
        public async Task EarlierUpstreamWinsForSameVersion()
        {
            First.Setup(u => u.ListPackagesAsync(Id, Cancellation))
                .ReturnsAsync(new List<Package> { Package("1.0.0", "first"), Package("2.0.0", "first") });
            Second.Setup(u => u.ListPackagesAsync(Id, Cancellation))
                .ReturnsAsync(new List<Package> { Package("2.0.0", "second"), Package("3.0.0", "second") });

            var result = await Target.ListPackagesAsync(Id, Cancellation);

            Assert.Equal(
                new[] { ("1.0.0", "first"), ("2.0.0", "first"), ("3.0.0", "second") },
                result.Select(p => (p.Version.ToNormalizedString(), p.Description)));
        }

        [Fact]
        public async Task SkipsFailingUpstream()
        {
            First.Setup(u => u.ListPackagesAsync(Id, Cancellation))
                .ReturnsAsync(new List<Package> { Package("1.0.0", "first") });
            Second.Setup(u => u.ListPackagesAsync(Id, Cancellation))
                .ThrowsAsync(new HttpRequestException("Upstream is down"));

            var result = await Target.ListPackagesAsync(Id, Cancellation);

            Assert.Equal("first", Assert.Single(result).Description);
        }
    }

    public class DownloadPackageOrNullAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsFirstUpstreamThatHasPackage()
        {
            using var stream = new MemoryStream();
            First.Setup(u => u.DownloadPackageOrNullAsync(Id, Version, Cancellation)).ReturnsAsync((Stream)null);
            Second.Setup(u => u.DownloadPackageOrNullAsync(Id, Version, Cancellation)).ReturnsAsync(stream);

            var result = await Target.DownloadPackageOrNullAsync(Id, Version, Cancellation);

            Assert.Same(stream, result);
        }

        [Fact]
        public async Task DoesNotQueryLaterUpstreamsOnceFound()
        {
            using var stream = new MemoryStream();
            First.Setup(u => u.DownloadPackageOrNullAsync(Id, Version, Cancellation)).ReturnsAsync(stream);

            var result = await Target.DownloadPackageOrNullAsync(Id, Version, Cancellation);

            Assert.Same(stream, result);
            Second.Verify(
                u => u.DownloadPackageOrNullAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task SkipsFailingUpstream()
        {
            using var stream = new MemoryStream();
            First.Setup(u => u.DownloadPackageOrNullAsync(Id, Version, Cancellation))
                .ThrowsAsync(new HttpRequestException("Upstream is down"));
            Second.Setup(u => u.DownloadPackageOrNullAsync(Id, Version, Cancellation)).ReturnsAsync(stream);

            var result = await Target.DownloadPackageOrNullAsync(Id, Version, Cancellation);

            Assert.Same(stream, result);
        }

        [Fact]
        public async Task ReturnsNullWhenNoUpstreamHasPackage()
        {
            First.Setup(u => u.DownloadPackageOrNullAsync(Id, Version, Cancellation)).ReturnsAsync((Stream)null);
            Second.Setup(u => u.DownloadPackageOrNullAsync(Id, Version, Cancellation)).ReturnsAsync((Stream)null);

            var result = await Target.DownloadPackageOrNullAsync(Id, Version, Cancellation);

            Assert.Null(result);
        }

        [Fact]
        public async Task RethrowsWhenCancelled()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            First.Setup(u => u.DownloadPackageOrNullAsync(Id, Version, cts.Token))
                .ThrowsAsync(new OperationCanceledException(cts.Token));

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => Target.DownloadPackageOrNullAsync(Id, Version, cts.Token));
        }
    }

    public class GetServiceIndexUrl : FactsBase
    {
        [Fact]
        public void ReturnsFirstUpstreamBeforeAnyDownload()
        {
            Assert.Equal("https://first.test/v3/index.json", Target.GetServiceIndexUrl());
        }

        [Fact]
        public async Task ReturnsUpstreamThatServedTheDownload()
        {
            using var stream = new MemoryStream();
            First.Setup(u => u.DownloadPackageOrNullAsync(Id, Version, Cancellation)).ReturnsAsync((Stream)null);
            Second.Setup(u => u.DownloadPackageOrNullAsync(Id, Version, Cancellation)).ReturnsAsync(stream);

            await Target.DownloadPackageOrNullAsync(Id, Version, Cancellation);

            Assert.Equal("https://second.test/v3/index.json", Target.GetServiceIndexUrl());
        }
    }

    public class FactsBase
    {
        protected static readonly string Id = "Package";
        protected static readonly NuGetVersion Version = new("2.0.0");
        protected static readonly CancellationToken Cancellation = CancellationToken.None;

        protected readonly Mock<IUpstreamClient> First = new();
        protected readonly Mock<IUpstreamClient> Second = new();
        protected readonly FallbackUpstreamClient Target;

        protected FactsBase()
        {
            First.Setup(u => u.GetServiceIndexUrl()).Returns("https://first.test/v3/index.json");
            Second.Setup(u => u.GetServiceIndexUrl()).Returns("https://second.test/v3/index.json");

            Target = new FallbackUpstreamClient(
                [First.Object, Second.Object],
                NullLogger<FallbackUpstreamClient>.Instance);
        }

        protected static IReadOnlyList<NuGetVersion> Versions(params string[] versions)
        {
            return versions.Select(NuGetVersion.Parse).ToList();
        }

        protected static Package Package(string version, string description)
        {
            return new Package
            {
                Id = Id,
                Version = NuGetVersion.Parse(version),
                Description = description,
            };
        }
    }
}
