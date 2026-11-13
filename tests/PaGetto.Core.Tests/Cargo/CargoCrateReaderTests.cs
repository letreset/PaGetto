using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Cargo;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Cargo;

public class CargoCrateReaderTests
{
    public class ReadAsync
    {
        [Fact]
        public async Task ReturnsTheReadme()
        {
            var crate = CargoCrateBuilder.Create("My_Crate", "1.0.0", readme: "# Hello");

            var readme = await CargoCrateReader.ReadAsync(new MemoryStream(crate), "My_Crate", "1.0.0", CancellationToken.None);

            Assert.Equal("# Hello", readme);
        }

        [Fact]
        public async Task ReturnsNullWithoutAReadme()
        {
            var crate = CargoCrateBuilder.Create("my_crate", "1.0.0");

            Assert.Null(await CargoCrateReader.ReadAsync(new MemoryStream(crate), "my_crate", "1.0.0", CancellationToken.None));
        }

        [Theory]
        [InlineData("other_crate", "1.0.0")]
        [InlineData("my_crate", "2.0.0")]
        public async Task RejectsTheCrateOfAnotherNameOrVersion(string name, string version)
        {
            var crate = CargoCrateBuilder.Create("my_crate", "1.0.0");

            var exception = await Assert.ThrowsAsync<InvalidCargoPackageException>(
                () => CargoCrateReader.ReadAsync(new MemoryStream(crate), name, version, CancellationToken.None));

            Assert.Contains($"{name}-{version}/Cargo.toml", exception.Message);
        }

        [Fact]
        public async Task RejectsACrateWithoutCargoToml()
        {
            var crate = PubArchiveBuilder.Create(new Dictionary<string, string> { ["my_crate-1.0.0/src/lib.rs"] = "" });

            await Assert.ThrowsAsync<InvalidCargoPackageException>(
                () => CargoCrateReader.ReadAsync(crate, "my_crate", "1.0.0", CancellationToken.None));
        }

        [Fact]
        public async Task RejectsDataThatIsNotGzip()
        {
            var exception = await Assert.ThrowsAsync<InvalidCargoPackageException>(
                () => CargoCrateReader.ReadAsync(new MemoryStream("not a crate"u8.ToArray()), "my_crate", "1.0.0", CancellationToken.None));

            Assert.Contains("gzipped tar", exception.Message);
        }
    }
}
