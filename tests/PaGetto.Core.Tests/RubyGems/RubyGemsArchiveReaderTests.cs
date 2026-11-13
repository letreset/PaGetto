using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.RubyGems;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.RubyGems;

public class RubyGemsArchiveReaderTests
{
    public class ReadAsync
    {
        [Fact]
        public async Task ReadsTheGemspecAndReadme()
        {
            var gem = RubyGemBuilder.Create("my_gem", "1.0.0", "java", readme: "# My gem");

            var archive = await RubyGemsArchiveReader.ReadAsync(new MemoryStream(gem), CancellationToken.None);

            Assert.Equal("my_gem", archive.Specification.Name);
            Assert.Equal("1.0.0", archive.Specification.Version);
            Assert.Equal("java", archive.Specification.Platform);
            Assert.Equal("# My gem", archive.Readme);
        }

        [Fact]
        public async Task GemWithoutReadmeHasNone()
        {
            var archive = await RubyGemsArchiveReader.ReadAsync(new MemoryStream(RubyGemBuilder.Create("my_gem", "1.0.0")), CancellationToken.None);

            Assert.Null(archive.Readme);
        }

        [Fact]
        public async Task UnreadableDataOnlyMeansNoReadme()
        {
            var gem = RubyGemBuilder.CreateTar(new Dictionary<string, byte[]>
            {
                ["metadata.gz"] = RubyGemBuilder.Gzip(Encoding.UTF8.GetBytes(RubyGemBuilder.Gemspec("my_gem", "1.0.0"))),
                ["data.tar.gz"] = "not gzip"u8.ToArray(),
            });

            var archive = await RubyGemsArchiveReader.ReadAsync(new MemoryStream(gem), CancellationToken.None);

            Assert.Equal("my_gem", archive.Specification.Name);
            Assert.Null(archive.Readme);
        }

        [Fact]
        public async Task GemWithoutMetadataIsRejected()
        {
            var gem = RubyGemBuilder.CreateTar(new Dictionary<string, byte[]> { ["data.tar.gz"] = [] });

            var e = await Assert.ThrowsAsync<InvalidRubyGemException>(() => RubyGemsArchiveReader.ReadAsync(new MemoryStream(gem), CancellationToken.None));
            Assert.Contains("metadata.gz", e.Message);
        }

        [Fact]
        public async Task MetadataThatIsNotGzipIsRejected()
        {
            var gem = RubyGemBuilder.CreateTar(new Dictionary<string, byte[]> { ["metadata.gz"] = "name: x"u8.ToArray() });

            var e = await Assert.ThrowsAsync<InvalidRubyGemException>(() => RubyGemsArchiveReader.ReadAsync(new MemoryStream(gem), CancellationToken.None));
            Assert.Contains("gzip", e.Message);
        }

        [Fact]
        public async Task FileThatIsNotATarArchiveIsRejected()
        {
            var e = await Assert.ThrowsAsync<InvalidRubyGemException>(
                () => RubyGemsArchiveReader.ReadAsync(new MemoryStream(Encoding.ASCII.GetBytes(new string('x', 2048))), CancellationToken.None));
            Assert.Contains("tar", e.Message);
        }

        [Fact]
        public async Task TruncatedGemIsRejected()
        {
            var gem = RubyGemBuilder.Create("my_gem", "1.0.0");

            await Assert.ThrowsAsync<InvalidRubyGemException>(() => RubyGemsArchiveReader.ReadAsync(new MemoryStream(gem[..700]), CancellationToken.None));
        }
    }
}
