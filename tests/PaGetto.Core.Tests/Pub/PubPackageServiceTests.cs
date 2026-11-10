using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Pub;
using PaGetto.Core.Tests.Support;
using PaGetto.Core.Upstream;
using Xunit;

namespace PaGetto.Core.Tests.Pub;

public class PubPackageServiceTests
{
    public class PublishAsync : FactsBase
    {
        [Fact]
        public async Task StoresAndIndexesThePackage()
        {
            using var archive = PubArchiveBuilder.Create("my_package", "1.2.3+4", readme: "# Hello");
            var expectedHash = Convert.ToHexStringLower(SHA256.HashData(archive.ToArray()));

            var result = await Target.PublishAsync(Feed, archive, Ct);

            Assert.Equal(PubPublishStatus.Success, result.Status);
            Assert.Equal("my_package", result.Name);
            Assert.Equal("1.2.3+4", result.Version);

            var version = await Context.ArtifactVersions.Include(v => v.Artifact).SingleAsync(Ct);
            Assert.Equal("my_package", version.Artifact.Name);
            Assert.Equal("1.2.3+4", version.Version);
            Assert.Equal("A test package.", version.Description);
            Assert.Equal(expectedHash, version.ContentHash);
            Assert.Equal(archive.Length, version.Size);
            Assert.True(version.Listed);
            Assert.True(version.HasReadme);
            Assert.False(version.IsPrerelease);
            Assert.Null(version.CachedFrom);
            Assert.Equal(Now, version.Published);
            Assert.Contains("\"name\":\"my_package\"", version.MetadataJson);

            Storage.Verify(
                s => s.SaveAsync("dart", "my_package", "1.2.3+4", archive, It.IsNotNull<Stream>(), Ct),
                Times.Once);
        }

        [Fact]
        public async Task RejectsAnInvalidArchive()
        {
            using var archive = new MemoryStream("not an archive"u8.ToArray());

            var result = await Target.PublishAsync(Feed, archive, Ct);

            Assert.Equal(PubPublishStatus.InvalidPackage, result.Status);
            Assert.False(string.IsNullOrEmpty(result.Message));
            Assert.Equal(0, await Context.ArtifactVersions.CountAsync(Ct));
            Storage.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task RejectsAVersionThatExists()
        {
            using var first = PubArchiveBuilder.Create("my_package", "1.0.0");
            using var second = PubArchiveBuilder.Create("my_package", "1.0.0", readme: "changed");
            await Target.PublishAsync(Feed, first, Ct);

            var result = await Target.PublishAsync(Feed, second, Ct);

            Assert.Equal(PubPublishStatus.AlreadyExists, result.Status);
            Assert.Contains("already exists", result.Message);
            Assert.False((await Context.ArtifactVersions.SingleAsync(Ct)).HasReadme);
        }

        [Theory]
        [InlineData(PackageOverwriteAllowed.True, "1.0.0", true)]
        [InlineData(PackageOverwriteAllowed.PrereleaseOnly, "1.0.0-dev.1", true)]
        [InlineData(PackageOverwriteAllowed.PrereleaseOnly, "1.0.0", false)]
        public async Task ReplacesAVersionWhenTheFeedAllowsOverwrites(PackageOverwriteAllowed allowed, string version, bool replaced)
        {
            FeedSettings.Setup(s => s.GetAllowPackageOverwrites(Feed)).Returns(allowed);

            using var first = PubArchiveBuilder.Create("my_package", version);
            using var second = PubArchiveBuilder.Create("my_package", version, readme: "changed");
            await Target.PublishAsync(Feed, first, Ct);

            var result = await Target.PublishAsync(Feed, second, Ct);

            Assert.Equal(replaced ? PubPublishStatus.Success : PubPublishStatus.AlreadyExists, result.Status);
            Assert.Equal(replaced, (await Context.ArtifactVersions.SingleAsync(Ct)).HasReadme);
            Storage.Verify(s => s.DeleteAsync("dart", "my_package", version, Ct), replaced ? Times.Once : Times.Never);
        }
    }

    public class FindPackageOrNullAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsNullForAnUnknownOrInvalidName()
        {
            Assert.Null(await Target.FindPackageOrNullAsync(Feed, "unknown", Ct));
            Assert.Null(await Target.FindPackageOrNullAsync(Feed, "Not-A-Name", Ct));
        }

        [Fact]
        public async Task ListsVersionsOldestFirstWithTheLatestStable()
        {
            foreach (var version in new[] { "2.0.0-dev.1", "1.0.0+2", "1.0.0", "1.10.0", "1.9.0" })
            {
                using var archive = PubArchiveBuilder.Create("my_package", version);
                await Target.PublishAsync(Feed, archive, Ct);
            }

            var package = await Target.FindPackageOrNullAsync(Feed, "my_package", Ct);

            Assert.Equal(new[] { "1.0.0", "1.0.0+2", "1.9.0", "1.10.0", "2.0.0-dev.1" }, package.Versions.Select(v => v.Version));
            Assert.Equal("1.10.0", package.Latest.Version);
        }

        [Fact]
        public async Task SkipsRetractedVersionsForTheLatest()
        {
            foreach (var version in new[] { "1.0.0", "2.0.0" })
            {
                using var archive = PubArchiveBuilder.Create("my_package", version);
                await Target.PublishAsync(Feed, archive, Ct);
            }

            await Artifacts.SetListedAsync(Feed.Id, "my_package", "2.0.0", listed: false, Ct);

            var package = await Target.FindPackageOrNullAsync(Feed, "my_package", Ct);

            Assert.Equal("1.0.0", package.Latest.Version);
            Assert.True(package.Versions.Single(v => v.Version == "2.0.0").Retracted);
        }

        [Fact]
        public async Task CombinesMirrorVersionsWithPublishedOnesWhichWin()
        {
            using var archive = PubArchiveBuilder.Create("my_package", "1.0.0");
            await Target.PublishAsync(Feed, archive, Ct);

            Upstream
                .Setup(u => u.ListVersionsAsync(Feed, "my_package", Ct))
                .ReturnsAsync(
                [
                    UpstreamVersion("1.0.0", sha256: "upstream-hash"),
                    UpstreamVersion("2.0.0", sha256: "upstream-hash"),
                ]);

            var package = await Target.FindPackageOrNullAsync(Feed, "my_package", Ct);

            Assert.Equal(new[] { "1.0.0", "2.0.0" }, package.Versions.Select(v => v.Version));
            Assert.NotEqual("upstream-hash", package.Versions[0].ArchiveSha256);
            Assert.Equal("upstream-hash", package.Versions[1].ArchiveSha256);
        }
    }

    public class GetArchiveStreamOrNullAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsNullForAnUnknownVersion()
        {
            Assert.Null(await Target.GetArchiveStreamOrNullAsync(Feed, "my_package", "1.0.0", Ct));
            Assert.Null(await Target.GetArchiveStreamOrNullAsync(Feed, "my_package", "not-a-version", Ct));
            Assert.Null(await Target.GetArchiveStreamOrNullAsync(Feed, "../evil", "1.0.0", Ct));
        }

        [Fact]
        public async Task OpensAPublishedArchiveAndCountsTheDownload()
        {
            using var archive = PubArchiveBuilder.Create("my_package", "1.0.0");
            await Target.PublishAsync(Feed, archive, Ct);

            var stored = new MemoryStream();
            Storage.Setup(s => s.GetArchiveStreamAsync("dart", "my_package", "1.0.0", Ct)).ReturnsAsync(stored);

            var result = await Target.GetArchiveStreamOrNullAsync(Feed, "my_package", "1.0.0", Ct);

            Assert.Same(stored, result);
            Assert.Equal(1, await Context.ArtifactVersions.AsNoTracking().Select(v => v.Downloads).SingleAsync(Ct));
        }

        [Fact]
        public async Task MirrorsAVersionOnItsFirstDownload()
        {
            var bytes = PubArchiveBuilder.Create("my_package", "2.0.0", readme: "# Upstream").ToArray();
            var upstreamVersion = UpstreamVersion("2.0.0", sha256: Convert.ToHexStringLower(SHA256.HashData(bytes)), retracted: true);

            Upstream.Setup(u => u.ListVersionsAsync(Feed, "my_package", Ct)).ReturnsAsync([upstreamVersion]);
            Upstream
                .Setup(u => u.DownloadArchiveOrNullAsync(Feed, upstreamVersion, Ct))
                .ReturnsAsync(() => new MemoryStream(bytes));

            await Target.GetArchiveStreamOrNullAsync(Feed, "my_package", "2.0.0", Ct);
            await Target.GetArchiveStreamOrNullAsync(Feed, "my_package", "2.0.0", Ct);

            var version = await Context.ArtifactVersions.AsNoTracking().SingleAsync(Ct);
            Assert.Equal("2.0.0", version.Version);
            Assert.Equal("https://pub.example.com/", version.CachedFrom);
            Assert.Equal(upstreamVersion.PubspecJson, version.MetadataJson);
            Assert.Equal(upstreamVersion.Published, version.Published);
            Assert.False(version.Listed);
            Assert.True(version.HasReadme);
            Assert.Equal(2, version.Downloads);

            // The second download is served from the feed.
            Upstream.Verify(u => u.DownloadArchiveOrNullAsync(Feed, upstreamVersion, Ct), Times.Once);
        }

        [Fact]
        public async Task RefusesAMirroredArchiveWithTheWrongHash()
        {
            var upstreamVersion = UpstreamVersion("2.0.0", sha256: new string('0', 64));

            Upstream.Setup(u => u.ListVersionsAsync(Feed, "my_package", Ct)).ReturnsAsync([upstreamVersion]);
            Upstream
                .Setup(u => u.DownloadArchiveOrNullAsync(Feed, upstreamVersion, Ct))
                .ReturnsAsync(() => PubArchiveBuilder.Create("my_package", "2.0.0"));

            Assert.Null(await Target.GetArchiveStreamOrNullAsync(Feed, "my_package", "2.0.0", Ct));
            Assert.Equal(0, await Context.ArtifactVersions.CountAsync(Ct));
            Storage.Verify(
                s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<Stream>(), Ct),
                Times.Never);
        }
    }

    public class FactsBase : IDisposable
    {
        protected static readonly DateTime Now = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        protected readonly TestDbContext Context;
        protected readonly Feed Feed;
        protected readonly ArtifactDatabase Artifacts;
        protected readonly Mock<IArtifactStorageService> Storage = new();
        protected readonly Mock<PubUpstreamClient> Upstream;
        protected readonly Mock<IFeedSettingsResolver> FeedSettings = new();
        protected readonly PubPackageService Target;
        protected readonly CancellationToken Ct = CancellationToken.None;

        protected FactsBase()
        {
            Context = TestDbContext.Create();

            Feed = new Feed
            {
                Id = Guid.NewGuid(),
                Slug = "dart",
                Name = "Dart",
                Type = FeedType.Pub,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now,
            };
            Context.Feeds.Add(Feed);
            Context.SaveChanges();

            var time = new Mock<SystemTime>();
            time.Setup(t => t.UtcNow).Returns(Now);

            Upstream = new Mock<PubUpstreamClient>(
                FeedSettings.Object,
                Mock.Of<IUpstreamClientFactory>(),
                new UpstreamListingCache(),
                Mock.Of<ILogger<PubUpstreamClient>>());
            Upstream
                .Setup(u => u.ListVersionsAsync(It.IsAny<Feed>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            FeedSettings.Setup(s => s.GetAllowPackageOverwrites(Feed)).Returns(PackageOverwriteAllowed.False);

            Artifacts = new ArtifactDatabase(Context, time.Object);
            Target = new PubPackageService(
                Artifacts,
                Storage.Object,
                Upstream.Object,
                new PackageMirrorLock(),
                FeedSettings.Object,
                time.Object,
                Mock.Of<ILogger<PubPackageService>>());
        }

        protected static PubUpstreamVersion UpstreamVersion(string version, string sha256, bool retracted = false)
        {
            return new PubUpstreamVersion
            {
                Version = version,
                ArchiveUrl = $"https://pub.example.com/packages/my_package/versions/{version}.tar.gz",
                ArchiveSha256 = sha256,
                PubspecJson = $$"""{"name":"my_package","version":"{{version}}","description":"From upstream."}""",
                Retracted = retracted,
                Published = new DateTime(2025, 5, 6, 7, 8, 9, DateTimeKind.Utc),
                Mirror = new MirrorOptions { Enabled = true, PackageSource = new Uri("https://pub.example.com") },
            };
        }

        public void Dispose()
        {
            Context?.Dispose();
        }
    }
}
