using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Cran;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Cran;

public class CranArchiveReaderTests
{
    public class ReadAsync
    {
        [Fact]
        public async Task ReadsTheDescriptionAndReadme()
        {
            var archive = await ReadPackageAsync(CranPackageBuilder.Create("hello", "1.0-2", readme: "# Hello"));

            Assert.Equal("hello", archive.Name);
            Assert.Equal("1.0-2", archive.Version.Text);
            Assert.Equal("A Test Package", CranDcf.GetValue(archive.Description, "Title"));
            Assert.Equal("A package for tests. It says hello\nand does nothing else.", CranDcf.GetValue(archive.Description, "Description"));
            Assert.Equal("# Hello", archive.Readme);
        }

        [Fact]
        public async Task IgnoresDescriptionFilesDeeperInThePackage()
        {
            var files = new Dictionary<string, string>
            {
                ["hello/inst/extdata/DESCRIPTION"] = "Package: other\nVersion: 9.9\n",
                ["hello/DESCRIPTION"] = CranPackageBuilder.Description("hello", "1.0"),
            };

            var archive = await ReadPackageAsync(PubArchiveBuilder.Create(files).ToArray());

            Assert.Equal("hello", archive.Name);
            Assert.Null(archive.Readme);
        }

        [Fact]
        public async Task ReadsADescriptionInLatin1()
        {
            var description = Encoding.Latin1.GetBytes("Package: hello\nVersion: 1.0\nAuthor: José Müller\nEncoding: latin1\n");

            // The builder writes UTF-8, so the Latin-1 bytes are written by hand.
            var archive = await ReadPackageAsync(BuildWithBytes("hello/DESCRIPTION", description));

            Assert.Equal("José Müller", CranDcf.GetValue(archive.Description, "Author"));
        }

        public static IEnumerable<object[]> InvalidPackages()
        {
            yield return [new byte[] { 1, 2, 3 }, "not a valid gzipped tar archive"];
            yield return [PubArchiveBuilder.Create(new Dictionary<string, string> { ["hello/R/hello.R"] = "" }).ToArray(), "no DESCRIPTION file"];
            yield return [PubArchiveBuilder.Create(new Dictionary<string, string> { ["DESCRIPTION"] = CranPackageBuilder.Description("hello", "1.0") }).ToArray(), "no DESCRIPTION file"];
            yield return [CranPackageBuilder.Create("hello", "1.0", folder: "hello-1.0"), "is in the folder hello-1.0, not hello"];
            yield return [CranPackageBuilder.Create("hello", "1.0", description: "Version: 1.0\n"), "no Package field"];
            yield return [CranPackageBuilder.Create("hello", "1.0", description: "Package: hello\n"), "no Version field"];
            yield return [CranPackageBuilder.Create("hello", "1.0", description: "Package: hello\nVersion: 1.0-beta\n"), "the version '1.0-beta'"];
            yield return [CranPackageBuilder.Create("hello_world", "1.0"), "the package name 'hello_world'"];
            yield return [CranPackageBuilder.Create("hello", "1.0", description: "Package: hello\nnot a field\n"), "DESCRIPTION file is not valid"];
        }

        [Theory]
        [MemberData(nameof(InvalidPackages))]
        public async Task RejectsInvalidPackagesWithAMessage(byte[] package, string message)
        {
            var exception = await Assert.ThrowsAsync<InvalidCranPackageException>(() => ReadPackageAsync(package));

            Assert.Contains(message, exception.Message);
        }

        private static async Task<CranArchive> ReadPackageAsync(byte[] package)
        {
            using var stream = new MemoryStream(package);
            return await CranArchiveReader.ReadAsync(stream, CancellationToken.None);
        }

        private static byte[] BuildWithBytes(string path, byte[] content)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            using (var tar = new TarWriter(gzip, TarEntryFormat.Ustar))
            {
                var entry = new UstarTarEntry(TarEntryType.RegularFile, path)
                {
                    DataStream = new MemoryStream(content),
                };
                tar.WriteEntry(entry);
            }

            return output.ToArray();
        }
    }
}
