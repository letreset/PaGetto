using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using Xunit;

namespace PaGetto.Core.Tests.Artifacts;

public class ArtifactPublisherTests
{
    public class AddAsync
    {
        private readonly Mock<IArtifactDatabase> _artifacts = new();
        private readonly Mock<IArtifactStorageService> _storage = new();
        private readonly Feed _feed = new() { Id = Guid.NewGuid(), Slug = "dart" };

        [Fact]
        public async Task StoresTheContentThenAddsTheVersion()
        {
            _artifacts.Setup(a => a.AddVersionAsync(_feed.Id, "pkg", It.IsAny<ArtifactVersion>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArtifactAddResult.Success);
            var version = new ArtifactVersion { Version = "1.0.0" };
            using var archive = new MemoryStream([1, 2, 3]);
            archive.Position = 3;

            var result = await ArtifactPublisher.AddAsync(_artifacts.Object, _storage.Object, _feed, "pkg", version, archive, "# Readme", default);

            Assert.Equal(ArtifactAddResult.Success, result);
            Assert.True(version.HasReadme);
            _storage.Verify(s => s.SaveAsync("dart", "pkg", "1.0.0", It.Is<Stream>(a => a == archive && a.Position == 0), It.IsNotNull<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RemovesTheContentWhenTheVersionCannotBeAdded()
        {
            _artifacts.Setup(a => a.AddVersionAsync(_feed.Id, "pkg", It.IsAny<ArtifactVersion>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("The database is unavailable."));
            using var archive = new MemoryStream([1, 2, 3]);

            await Assert.ThrowsAsync<InvalidOperationException>(() => ArtifactPublisher.AddAsync(
                _artifacts.Object, _storage.Object, _feed, "pkg", new ArtifactVersion { Version = "1.0.0" }, archive, readme: null, default));

            // Otherwise the storage would refuse every later publish of this version with other content.
            _storage.Verify(s => s.DeleteAsync("dart", "pkg", "1.0.0", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task KeepsTheContentOfAVersionAnotherPublishAdded()
        {
            _artifacts.Setup(a => a.AddVersionAsync(_feed.Id, "pkg", It.IsAny<ArtifactVersion>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("The database is unavailable."));
            _artifacts.Setup(a => a.FindVersionOrNullAsync(_feed.Id, "pkg", "1.0.0", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ArtifactVersion { Version = "1.0.0" });
            using var archive = new MemoryStream([1, 2, 3]);

            await Assert.ThrowsAsync<InvalidOperationException>(() => ArtifactPublisher.AddAsync(
                _artifacts.Object, _storage.Object, _feed, "pkg", new ArtifactVersion { Version = "1.0.0" }, archive, readme: null, default));

            _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task KeepsTheContentWhenTheVersionAlreadyExists()
        {
            _artifacts.Setup(a => a.AddVersionAsync(_feed.Id, "pkg", It.IsAny<ArtifactVersion>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArtifactAddResult.AlreadyExists);
            using var archive = new MemoryStream([1, 2, 3]);

            var result = await ArtifactPublisher.AddAsync(
                _artifacts.Object, _storage.Object, _feed, "pkg", new ArtifactVersion { Version = "1.0.0" }, archive, readme: null, default);

            Assert.Equal(ArtifactAddResult.AlreadyExists, result);
            _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task StoresNothingForAVersionWithoutAnArchive()
        {
            _artifacts.Setup(a => a.AddVersionAsync(_feed.Id, "pkg", It.IsAny<ArtifactVersion>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("The database is unavailable."));

            await Assert.ThrowsAsync<InvalidOperationException>(() => ArtifactPublisher.AddAsync(
                _artifacts.Object, _storage.Object, _feed, "pkg", new ArtifactVersion { Version = "1.0.0" }, archive: null, readme: null, default));

            _storage.VerifyNoOtherCalls();
        }
    }
}
