using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Cargo;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Cargo;

public class CargoPublishRequestTests
{
    public class ReadAsync
    {
        [Fact]
        public async Task ReadsTheMetadataAndTheCrate()
        {
            var crate = CargoCrateBuilder.Create("my_crate", "1.0.0");
            var body = CargoCrateBuilder.PublishBody(CargoCrateBuilder.Metadata("my_crate", "1.0.0"), crate);

            using var request = await CargoPublishRequest.ReadAsync(new MemoryStream(body), 1024 * 1024, CancellationToken.None);

            Assert.Equal("my_crate", request.Metadata["name"]!.GetValue<string>());
            Assert.Equal("1.0.0", request.Metadata["vers"]!.GetValue<string>());

            using var content = new MemoryStream();
            await request.Crate.CopyToAsync(content);
            Assert.Equal(crate, content.ToArray());
        }

        [Fact]
        public async Task RejectsACrateLargerThanTheLimit()
        {
            var crate = CargoCrateBuilder.Create("my_crate", "1.0.0");
            var body = CargoCrateBuilder.PublishBody(CargoCrateBuilder.Metadata("my_crate", "1.0.0"), crate);

            await Assert.ThrowsAsync<ArtifactFileTooLargeException>(
                () => CargoPublishRequest.ReadAsync(new MemoryStream(body), crate.Length - 1, CancellationToken.None));
        }

        [Fact]
        public async Task RejectsATruncatedBody()
        {
            var body = CargoCrateBuilder.PublishBody(CargoCrateBuilder.Metadata("my_crate", "1.0.0"), CargoCrateBuilder.Create("my_crate", "1.0.0"));

            var exception = await Assert.ThrowsAsync<InvalidCargoPackageException>(
                () => CargoPublishRequest.ReadAsync(new MemoryStream(body[..^10]), 1024 * 1024, CancellationToken.None));

            Assert.Contains("ends before", exception.Message);
        }

        [Fact]
        public async Task RejectsMetadataThatIsNotAJsonObject()
        {
            var body = CargoCrateBuilder.PublishBody(CargoCrateBuilder.Metadata("my_crate", "1.0.0"), []);
            body[4] = (byte)'[';

            await Assert.ThrowsAsync<InvalidCargoPackageException>(
                () => CargoPublishRequest.ReadAsync(new MemoryStream(body), 1024 * 1024, CancellationToken.None));
        }

        [Fact]
        public async Task RejectsHugeMetadataBeforeReadingIt()
        {
            var body = new byte[] { 0xff, 0xff, 0xff, 0x7f };

            var exception = await Assert.ThrowsAsync<InvalidCargoPackageException>(
                () => CargoPublishRequest.ReadAsync(new MemoryStream(body), 1024 * 1024, CancellationToken.None));

            Assert.Contains("MiB", exception.Message, StringComparison.Ordinal);
        }
    }
}
