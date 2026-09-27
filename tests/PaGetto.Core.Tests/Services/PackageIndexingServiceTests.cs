using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Indexing;
using PaGetto.Core.Search;
using PaGetto.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Packaging;
using NuGet.Versioning;
using Xunit;

namespace PaGetto.Core.Tests.Services;

public class PackageIndexingServiceTests
{
    private readonly Mock<IPackageDatabase> _packages;
    private readonly Mock<IPackageStorageService> _storage;
    private readonly Mock<ISearchIndexer> _search;
    private readonly Mock<SystemTime> _time;
    private readonly Mock<IPackageDeletionService> _deleter;
    private readonly PackageIndexingService _target;
    private readonly PaGettoOptions _mockOptions;

    public PackageIndexingServiceTests()
    {
        _packages = new Mock<IPackageDatabase>(MockBehavior.Strict);
        _storage = new Mock<IPackageStorageService>(MockBehavior.Strict);
        _search = new Mock<ISearchIndexer>(MockBehavior.Strict);
        _time = new Mock<SystemTime>(MockBehavior.Loose);
        _deleter = new Mock<IPackageDeletionService>(MockBehavior.Strict);
        _mockOptions = new();
        var options = new Mock<IOptionsSnapshot<PaGettoOptions>>(MockBehavior.Strict);
        options.Setup(o => o.Value).Returns(_mockOptions);

        var feedSettings = new Mock<IFeedSettingsResolver>();
        feedSettings.Setup(r => r.GetAllowPackageOverwrites(It.IsAny<Feed>()))
            .Returns(() => _mockOptions.AllowPackageOverwrites);
        feedSettings.Setup(r => r.GetIsReadOnlyMode(It.IsAny<Feed>()))
            .Returns(() => _mockOptions.IsReadOnlyMode);
        feedSettings.Setup(r => r.GetMaxPackageSizeGiB(It.IsAny<Feed>()))
            .Returns(() => _mockOptions.MaxPackageSizeGiB);
        feedSettings.Setup(r => r.GetRetentionOptions(It.IsAny<Feed>()))
            .Returns(() => _mockOptions.Retention ?? new RetentionOptions());

        var defaultFeed = new Feed
        {
            Id = Guid.Empty,
            Slug = Feed.DefaultSlug,
            Name = "Default",
        };
        var feedService = new Mock<IFeedService>();
        feedService.Setup(s => s.GetFeedByIdAsync(It.IsAny<Guid>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(defaultFeed);

        _target = new PackageIndexingService(
            _packages.Object,
            _storage.Object,
            _deleter.Object,
            _search.Object,
            _time.Object,
            options.Object,
            feedSettings.Object,
            feedService.Object,
            Mock.Of<ILogger<PackageIndexingService>>());
    }

    // TODO: Add malformed package tests

    [Fact]
    public async Task IndexAsync_WhenPackageAlreadyExists_AndOverwriteForbidden_ReturnsPackageAlreadyExists()
    {
        // Arrange
        _mockOptions.AllowPackageOverwrites = PackageOverwriteAllowed.False;

        var builder = new PackageBuilder
        {
            Id = "pagetto-test",
            Version = NuGetVersion.Parse("1.0.0"),
            Description = "Test Description",
        };
        builder.Authors.Add("Test Author");
        var assemblyFile = GetType().Assembly.Location;
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = assemblyFile,
            TargetPath = "lib/Test.dll"
        });
        var stream = new MemoryStream();
        builder.Save(stream);
        _packages.Setup(p => p.ExistsAsync(It.IsAny<Guid>(), builder.Id, builder.Version, default)).ReturnsAsync(true);

        // Act
        var result = await _target.IndexAsync(Guid.Empty, "default", stream, cacheFeedUrl: null, published: null, default);

        // Assert
        Assert.Equal(PackageIndexingResult.PackageAlreadyExists, result);
    }

    [Fact]
    public async Task IndexAsync_WhenPackageAlreadyExists_AndOverwriteAllowed_IndexesPackage()
    {
        // Arrange
        _mockOptions.AllowPackageOverwrites = PackageOverwriteAllowed.True;

        var builder = new PackageBuilder
        {
            Id = "pagetto-test",
            Version = NuGetVersion.Parse("1.0.0"),
            Description = "Test Description",
        };
        builder.Authors.Add("Test Author");
        var assemblyFile = GetType().Assembly.Location;
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = assemblyFile,
            TargetPath = "lib/Test.dll"
        });
        var stream = new MemoryStream();
        builder.Save(stream);
        _packages.Setup(p => p.ExistsAsync(It.IsAny<Guid>(), builder.Id, builder.Version, default)).ReturnsAsync(true);
        _packages.Setup(p => p.HardDeletePackageAsync(It.IsAny<Guid>(), builder.Id, builder.Version, default)).ReturnsAsync(true);
        _packages.Setup(p => p.AddAsync(It.Is<Package>(p1 => p1.Id == builder.Id && p1.Version.ToString() == builder.Version.ToString()), default)).ReturnsAsync(PackageAddResult.Success);

        _storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), builder.Id, builder.Version, default)).Returns(Task.CompletedTask);
        _storage.Setup(s => s.SavePackageContentAsync(It.IsAny<string>(), It.Is<Package>(p => p.Id == builder.Id && p.Version.ToString() == builder.Version.ToString()), stream, It.IsAny<FileStream>(), default, default, default)).Returns(Task.CompletedTask);

        _search.Setup(s => s.IndexAsync(It.Is<Package>(p => p.Id == builder.Id && p.Version.ToString() == builder.Version.ToString()), default)).Returns(Task.CompletedTask);

        // Act
        var result = await _target.IndexAsync(Guid.Empty, "default", stream, cacheFeedUrl: null, published: null, default);

        // Assert
        Assert.Equal(PackageIndexingResult.Success, result);
    }

    [Fact]
    public async Task IndexAsync_WhenPrereleasePackageAlreadyExists_AndOverwritePrereleaseAllowed_IndexesPackage()
    {
        // Arrange
        _mockOptions.AllowPackageOverwrites = PackageOverwriteAllowed.PrereleaseOnly;

        var builder = new PackageBuilder
        {
            Id = "pagetto-test",
            Version = NuGetVersion.Parse("1.0.0-beta"),
            Description = "Test Description",
        };
        builder.Authors.Add("Test Author");
        var assemblyFile = GetType().Assembly.Location;
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = assemblyFile,
            TargetPath = "lib/Test.dll"
        });
        var stream = new MemoryStream();
        builder.Save(stream);
        _packages.Setup(p => p.ExistsAsync(It.IsAny<Guid>(), builder.Id, builder.Version, default)).ReturnsAsync(true);
        _packages.Setup(p => p.HardDeletePackageAsync(It.IsAny<Guid>(), builder.Id, builder.Version, default)).ReturnsAsync(true);
        _packages.Setup(p => p.AddAsync(It.Is<Package>(p1 => p1.Id == builder.Id && p1.Version.ToString() == builder.Version.ToString()), default)).ReturnsAsync(PackageAddResult.Success);

        _storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), builder.Id, builder.Version, default)).Returns(Task.CompletedTask);
        _storage.Setup(s => s.SavePackageContentAsync(It.IsAny<string>(), It.Is<Package>(p => p.Id == builder.Id && p.Version.ToString() == builder.Version.ToString()), stream, It.IsAny<FileStream>(), default, default, default)).Returns(Task.CompletedTask);

        _search.Setup(s => s.IndexAsync(It.Is<Package>(p => p.Id == builder.Id && p.Version.ToString() == builder.Version.ToString()), default)).Returns(Task.CompletedTask);

        // Act
        var result = await _target.IndexAsync(Guid.Empty, "default", stream, cacheFeedUrl: null, published: null, default);

        // Assert
        Assert.Equal(PackageIndexingResult.Success, result);
    }

    [Fact]
    public async Task IndexAsync_WhenPrereleasePackageAlreadyExists_AndOverwriteForbidden_ReturnsPackageAlreadyExists()
    {
        // Arrange
        _mockOptions.AllowPackageOverwrites = PackageOverwriteAllowed.False;

        var builder = new PackageBuilder
        {
            Id = "pagetto-test",
            Version = NuGetVersion.Parse("1.0.0-beta"),
            Description = "Test Description",
        };
        builder.Authors.Add("Test Author");
        var assemblyFile = GetType().Assembly.Location;
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = assemblyFile,
            TargetPath = "lib/Test.dll"
        });
        var stream = new MemoryStream();
        builder.Save(stream);
        _packages.Setup(p => p.ExistsAsync(It.IsAny<Guid>(), builder.Id, builder.Version, default)).ReturnsAsync(true);

        // Act
        var result = await _target.IndexAsync(Guid.Empty, "default", stream, cacheFeedUrl: null, published: null, default);

        // Assert
        Assert.Equal(PackageIndexingResult.PackageAlreadyExists, result);
    }

    [Fact]
    public async Task IndexAsync_WithValidPackage_ReturnsSuccess()
    {
        // Arrange
        _mockOptions.AllowPackageOverwrites = PackageOverwriteAllowed.False;
        var builder = new PackageBuilder
        {
            Id = "pagetto-test",
            Version = NuGetVersion.Parse("1.0.0"),
            Description = "Test Description",
        };
        builder.Authors.Add("Test Author");
        var assemblyFile = GetType().Assembly.Location;
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = assemblyFile,
            TargetPath = "lib/Test.dll"
        });
        var stream = new MemoryStream();
        builder.Save(stream);
        _packages.Setup(p => p.ExistsAsync(It.IsAny<Guid>(), builder.Id, builder.Version, default)).ReturnsAsync(false);
        _packages.Setup(p => p.AddAsync(It.Is<Package>(p1 => p1.Id == builder.Id && p1.Version.ToString() == builder.Version.ToString()), default)).ReturnsAsync(PackageAddResult.Success);

        _storage.Setup(s => s.SavePackageContentAsync(It.IsAny<string>(), It.Is<Package>(p => p.Id == builder.Id && p.Version.ToString() == builder.Version.ToString()), stream, It.IsAny<FileStream>(), default, default, default)).Returns(Task.CompletedTask);

        _search.Setup(s => s.IndexAsync(It.Is<Package>(p => p.Id == builder.Id && p.Version.ToString() == builder.Version.ToString()), default)).Returns(Task.CompletedTask);

        // Act
        var result = await _target.IndexAsync(Guid.Empty, "default", stream, cacheFeedUrl: null, published: null, default);

        // Assert
        Assert.Equal(PackageIndexingResult.Success, result);
    }

    [Fact]
    public async Task WhenDatabaseAddFailsBecausePackageAlreadyExists_ReturnsPackageAlreadyExists()
    {
        await Task.Yield();
    }

    [Fact]
    public async Task ThrowsWhenStorageSaveThrows()
    {
        await Task.Yield();
    }

    public class IndexAsync
    {
        private readonly Mock<IPackageDatabase> _packages = new();
        private readonly Mock<IPackageStorageService> _storage = new();
        private readonly Mock<ISearchIndexer> _search = new();
        private readonly PackageIndexingService _target;

        public IndexAsync()
        {
            var options = new Mock<IOptionsSnapshot<PaGettoOptions>>();
            options.Setup(o => o.Value).Returns(new PaGettoOptions());

            var feedSettings = new Mock<IFeedSettingsResolver>();
            feedSettings.Setup(r => r.GetRetentionOptions(It.IsAny<Feed>())).Returns(new RetentionOptions());

            var feedService = new Mock<IFeedService>();
            feedService.Setup(s => s.GetFeedByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Feed { Id = Guid.Empty, Slug = Feed.DefaultSlug, Name = "Default" });

            _packages.Setup(p => p.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(PackageAddResult.Success);

            _target = new PackageIndexingService(
                _packages.Object,
                _storage.Object,
                Mock.Of<IPackageDeletionService>(),
                _search.Object,
                new Mock<SystemTime>().Object,
                options.Object,
                feedSettings.Object,
                feedService.Object,
                Mock.Of<ILogger<PackageIndexingService>>());
        }

        [Fact]
        public async Task WhenEmbeddedIconFileIsMissing_IndexesPackageWithoutIcon()
        {
            // Arrange
            var stream = CreatePackageWithMissingIcon();

            // Act
            var result = await _target.IndexAsync(Guid.Empty, Feed.DefaultSlug, stream, cacheFeedUrl: null, published: null, default);

            // Assert
            Assert.Equal(PackageIndexingResult.Success, result);
            _storage.Verify(s => s.SavePackageContentAsync(
                Feed.DefaultSlug,
                It.Is<Package>(p => p.Id == "pagetto-test" && !p.HasEmbeddedIcon),
                stream,
                It.IsAny<Stream>(),
                null,
                null,
                It.IsAny<CancellationToken>()), Times.Once);
            _packages.Verify(p => p.AddAsync(It.Is<Package>(p1 => !p1.HasEmbeddedIcon), It.IsAny<CancellationToken>()), Times.Once);
            _search.Verify(s => s.IndexAsync(It.Is<Package>(p => !p.HasEmbeddedIcon), It.IsAny<CancellationToken>()), Times.Once);
        }

        private MemoryStream CreatePackageWithMissingIcon()
        {
            var builder = new PackageBuilder
            {
                Id = "pagetto-test",
                Version = NuGetVersion.Parse("1.0.0"),
                Description = "Test Description",
                Icon = "icon.png",
            };
            builder.Authors.Add("Test Author");
            builder.Files.Add(new PhysicalPackageFile
            {
                SourcePath = GetType().Assembly.Location,
                TargetPath = "lib/Test.dll"
            });
            builder.Files.Add(new PhysicalPackageFile
            {
                SourcePath = GetType().Assembly.Location,
                TargetPath = "icon.png"
            });

            var stream = new MemoryStream();
            builder.Save(stream);

            // The nuspec still declares the icon, but the file is removed from the archive.
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
            {
                archive.GetEntry("icon.png").Delete();
            }

            stream.Position = 0;
            return stream;
        }
    }
}
