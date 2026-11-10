using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Pub;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Pub;

public class PubArchiveReaderTests
{
    public class ReadAsync
    {
        // contoso_e2e 1.0.0+1 as "dart pub publish --to-archive" of Dart 3.13.7 wrote it, base64 encoded.
        private const string DartClientArchive =
            ""
            + "H4sIAAAAAAAAA+2Yy27bMBBFs+ZXDOBFWhTWgzYtQEEXhqOmAZy0aLNuQlGTiDBFCiQd1H9fyHZa1wHqLioXCXg2eg00o8edueDs"
            + "4/T6oph/uoia6qQnAAAyxmBDsrcFgBGDlNFszDLKaAYAaTJmY0j6KmiXpfPcniTQLss/xh26/kIZDCCNkih5lxIyhA/SOg8WFXKH"
            + "EfnfxQV6Z345K66/Fr3mOKh/tq9/yCaMBv0fgavLG5hLgdohITPTrqx8qD28EW9hZrQ3zoBH56Hinod+8Ppol6VrUUQr3qi+chzS"
            + "/yhL9+d/StNR0P8R0LzBHMRG6rdIkTyiddLo/KcvqNAJK1u/PjmFlosFf8DuhSnpaqygXIGvESxyBRW3HoSSqD1IDVwD6mrozRB1"
            + "BaJGsQBzvw4/7yLvEauIWGyNk97YVQ61963L4xi/86ZVGAnTxLvlbdPeerOJzeNYGcFVbZzPGU2TuLuni7tCCOpHaY1uUPucALhq"
            + "kcO3UfdcoZdtULL/3/rg/H++P8kyCqz3yoL+lSx35RV1svnXOQ59/5SO9vs/ZTT4v2MQxzEUvzr02uspWVpuVxHZ7pwR0oXd1Ajb"
            + "4RARYbTzT4fwHk630+L0LHTWl8SXYnp+VfS4+PM3/X+SPFv/SWkS9H8EBr95P/J519Tdrc1cuyyfvN5dWBIKBAKB18IPy1nE7QAa"
            + "AAA=";

        [Fact]
        public async Task ReadsThePubspecAndTheReadme()
        {
            using var archive = PubArchiveBuilder.Create("my_package", "1.2.3+4", readme: "# Hello");

            var result = await PubArchiveReader.ReadAsync(archive, CancellationToken.None);

            Assert.Equal("my_package", result.Name);
            Assert.Equal("1.2.3+4", result.Version.ToString());
            Assert.Equal("A test package.", (string)result.Pubspec["description"]);
            Assert.Equal("# Hello", result.Readme);
        }

        [Fact]
        public async Task ReadsAnArchiveWrittenByTheDartClient()
        {
            // The pub client pads the numbers of its tar headers with spaces and writes the ustar
            // version "0 ", which System.Formats.Tar rejects.
            using var archive = new MemoryStream(Convert.FromBase64String(DartClientArchive));

            var result = await PubArchiveReader.ReadAsync(archive, CancellationToken.None);

            Assert.Equal("contoso_e2e", result.Name);
            Assert.Equal("1.0.0+1", result.Version.ToString());
            Assert.Equal("https://example.com/contoso_e2e", (string)result.Pubspec["repository"]);
            Assert.StartsWith("# contoso_e2e", result.Readme);
        }

        [Theory]
        [InlineData(TarEntryFormat.V7, 90)]
        [InlineData(TarEntryFormat.Ustar, 90)]
        [InlineData(TarEntryFormat.Ustar, 200)]
        [InlineData(TarEntryFormat.Pax, 200)]
        [InlineData(TarEntryFormat.Gnu, 200)]
        public async Task ReadsEveryTarFormatWithLongPaths(TarEntryFormat format, int pathLength)
        {
            // A long path is stored in another way by each format: the ustar prefix, a PAX header
            // or a GNU long name entry. The files after it must still be found.
            var longPath = "lib/" + string.Join("/", new string('a', (pathLength - 4) / 2), new string('b', (pathLength - 4) / 2));

            using var archive = PubArchiveBuilder.Create(
                new Dictionary<string, string>
                {
                    [longPath] = "// A file with a long path.\n",
                    ["README.md"] = "# Hello",
                    ["pubspec.yaml"] = "name: my_package\nversion: 1.0.0\n",
                },
                format);

            var result = await PubArchiveReader.ReadAsync(archive, CancellationToken.None);

            Assert.Equal("my_package", result.Name);
            Assert.Equal("# Hello", result.Readme);
        }

        [Fact]
        public async Task RejectsAHeaderWithAWrongChecksum()
        {
            using var valid = PubArchiveBuilder.Create("my_package", "1.0.0");
            using var tar = new MemoryStream();
            using (var gzip = new GZipStream(valid, CompressionMode.Decompress))
            {
                await gzip.CopyToAsync(tar);
            }

            // Change the first header without updating its checksum.
            var bytes = tar.ToArray();
            bytes[0] ^= 0x01;

            using var archive = new MemoryStream();
            using (var gzip = new GZipStream(archive, CompressionLevel.Fastest, leaveOpen: true))
            {
                gzip.Write(bytes);
            }

            archive.Position = 0;

            await Assert.ThrowsAsync<InvalidPubArchiveException>(
                () => PubArchiveReader.ReadAsync(archive, CancellationToken.None));
        }

        [Fact]
        public async Task ReadmeIsOptional()
        {
            using var archive = PubArchiveBuilder.Create("my_package", "1.0.0");

            var result = await PubArchiveReader.ReadAsync(archive, CancellationToken.None);

            Assert.Null(result.Readme);
        }

        [Fact]
        public async Task IgnoresAPubspecThatIsNotAtTheRoot()
        {
            using var archive = PubArchiveBuilder.Create(new Dictionary<string, string>
            {
                ["example/pubspec.yaml"] = "name: example\nversion: 1.0.0\n",
            });

            var exception = await Assert.ThrowsAsync<InvalidPubArchiveException>(
                () => PubArchiveReader.ReadAsync(archive, CancellationToken.None));

            Assert.Contains("pubspec.yaml", exception.Message);
        }

        [Theory]
        [InlineData("name: My-Package\nversion: 1.0.0\n")]
        [InlineData("name: ../evil\nversion: 1.0.0\n")]
        [InlineData("version: 1.0.0\n")]
        [InlineData("name: my_package\nversion: 1.0\n")]
        [InlineData("name: my_package\n")]
        public async Task RejectsAnInvalidNameOrVersion(string pubspec)
        {
            using var archive = PubArchiveBuilder.Create(new Dictionary<string, string>
            {
                ["pubspec.yaml"] = pubspec,
            });

            await Assert.ThrowsAsync<InvalidPubArchiveException>(
                () => PubArchiveReader.ReadAsync(archive, CancellationToken.None));
        }

        [Fact]
        public async Task RejectsWhatIsNotAnArchive()
        {
            using var archive = new MemoryStream("This is not a gzipped tar file."u8.ToArray());

            await Assert.ThrowsAsync<InvalidPubArchiveException>(
                () => PubArchiveReader.ReadAsync(archive, CancellationToken.None));
        }

        [Fact]
        public async Task RejectsATruncatedArchive()
        {
            using var complete = PubArchiveBuilder.Create("my_package", "1.0.0", readme: new string('x', 100_000));
            var bytes = complete.ToArray();
            using var archive = new MemoryStream(bytes, 0, bytes.Length / 2);

            await Assert.ThrowsAsync<InvalidPubArchiveException>(
                () => PubArchiveReader.ReadAsync(archive, CancellationToken.None));
        }
    }
}
