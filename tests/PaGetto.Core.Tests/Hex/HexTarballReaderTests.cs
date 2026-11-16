using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Hex;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Hex;

public class HexTarballReaderTests
{
    public class ReadAsync
    {
        [Fact]
        public async Task ReadsTheMetadataChecksumAndReadme()
        {
            var tarball = HexTarballBuilder.Create(
                "demo",
                "1.2.0",
                readme: "# Demo",
                requirements: $"[{HexTarballBuilder.Requirement("jason", "~> 1.4")},{HexTarballBuilder.Requirement("plug", "~> 1.0", repository: "internal", optional: true)}]");

            var (manifest, readme) = await HexTarballReader.ReadAsync(new MemoryStream(tarball), CancellationToken.None);

            Assert.Equal("demo", manifest.Name);
            Assert.Equal("1.2.0", manifest.Version);
            Assert.Equal("A test package.", manifest.Description);
            Assert.Equal("~> 1.15", manifest.Elixir);
            Assert.Equal(new[] { "MIT" }, manifest.Licenses);
            Assert.Equal(new[] { "mix" }, manifest.BuildTools);
            Assert.Equal("https://github.com/example/demo", manifest.Links["GitHub"]);
            Assert.Equal(64, manifest.InnerChecksum.Length);
            Assert.Equal("# Demo", readme);

            Assert.Collection(
                manifest.Requirements,
                r =>
                {
                    Assert.Equal("jason", r.Name);
                    Assert.Equal("~> 1.4", r.Requirement);
                    Assert.False(r.Optional);
                    Assert.Null(r.App);
                    Assert.Equal("hexpm", r.Repository);
                },
                r =>
                {
                    Assert.Equal("plug", r.Name);
                    Assert.True(r.Optional);
                    Assert.Equal("internal", r.Repository);
                });
        }

        [Fact]
        public async Task ReadsRequirementsKeyedByName()
        {
            var requirements = "[{<<\"jason\">>,[{<<\"app\">>,<<\"json\">>},{<<\"optional\">>,false},{<<\"requirement\">>,<<\"~> 1.4\">>}]}]";
            var tarball = HexTarballBuilder.Create("demo", "1.0.0", requirements: requirements);

            var (manifest, _) = await HexTarballReader.ReadAsync(new MemoryStream(tarball), CancellationToken.None);

            var requirement = Assert.Single(manifest.Requirements);
            Assert.Equal("jason", requirement.Name);
            Assert.Equal("json", requirement.App);
            Assert.Null(requirement.Repository);
        }

        [Fact]
        public async Task ReadsAPackageWithoutReadme()
        {
            var (manifest, readme) = await HexTarballReader.ReadAsync(new MemoryStream(HexTarballBuilder.Create("demo", "1.0.0", readme: null)), CancellationToken.None);

            Assert.Equal("demo", manifest.Name);
            Assert.Null(readme);
        }

        [Fact]
        public async Task RejectsAWrongChecksum()
        {
            var tarball = HexTarballBuilder.Create(HexTarballBuilder.Metadata("demo", "1.0.0"), Files(), checksum: new string('A', 64));

            var e = await Assert.ThrowsAsync<InvalidHexPackageException>(() => HexTarballReader.ReadAsync(new MemoryStream(tarball), CancellationToken.None));
            Assert.Contains("CHECKSUM", e.Message);
        }

        [Fact]
        public async Task RejectsAnotherFormatVersion()
        {
            var tarball = HexTarballBuilder.Create(HexTarballBuilder.Metadata("demo", "1.0.0"), Files(), formatVersion: "2");

            await Assert.ThrowsAsync<InvalidHexPackageException>(() => HexTarballReader.ReadAsync(new MemoryStream(tarball), CancellationToken.None));
        }

        [Fact]
        public async Task RejectsMetadataThatIsNotTerms()
        {
            var tarball = HexTarballBuilder.Create("{<<\"name\">>,<<\"demo\">>", Files());

            var e = await Assert.ThrowsAsync<InvalidHexPackageException>(() => HexTarballReader.ReadAsync(new MemoryStream(tarball), CancellationToken.None));
            Assert.Contains("metadata.config", e.Message);
        }

        [Fact]
        public async Task RejectsMetadataOfTheWrongShape()
        {
            var tarball = HexTarballBuilder.Create("{<<\"name\">>,[1]}.\n", Files());

            await Assert.ThrowsAsync<InvalidHexPackageException>(() => HexTarballReader.ReadAsync(new MemoryStream(tarball), CancellationToken.None));
        }

        [Fact]
        public async Task RejectsSomethingElse()
        {
            await Assert.ThrowsAsync<InvalidHexPackageException>(() => HexTarballReader.ReadAsync(new MemoryStream(new byte[2048]), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidHexPackageException>(() => HexTarballReader.ReadAsync(new MemoryStream([1, 2, 3]), CancellationToken.None));
        }

        private static Dictionary<string, string> Files()
        {
            return new Dictionary<string, string> { ["mix.exs"] = "defmodule Demo.MixProject do\nend\n" };
        }
    }
}
