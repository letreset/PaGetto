using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.PyPI;
using Xunit;

namespace PaGetto.Core.Tests.PyPI;

public class PyPIArchiveReaderTests
{
    public class ReadAsync
    {
        [Fact]
        public async Task ReadsTheMetadataOfAWheel()
        {
            var metadata = Metadata("My_Package", "1.0");
            using var wheel = Zip(
                ("my_package/__init__.py", ""),
                ("other-2.0.dist-info/METADATA", Metadata("other", "2.0")),
                ("my_package-1.0.dist-info/METADATA", metadata));

            var archive = await ReadArchiveAsync(wheel, "my_package-1.0-py3-none-any.whl", "my-package");

            Assert.Equal(metadata, Encoding.UTF8.GetString(archive.Metadata));
            Assert.Equal("My_Package", PyPICoreMetadata.GetString(archive.Manifest, "name"));
            Assert.Equal("A test package.", PyPICoreMetadata.GetString(archive.Manifest, "summary"));
            Assert.Equal("The readme.", archive.Readme);
            Assert.Equal(0, wheel.Position);
        }

        [Fact]
        public async Task ReadsThePkgInfoOfAGzippedSdist()
        {
            using var sdist = TarGz(
                ("my_package-1.0/setup.py", ""),
                ("my_package-1.0/src/my_package/PKG-INFO", Metadata("wrong", "9.9")),
                ("my_package-1.0/PKG-INFO", Metadata("my-package", "1.0")));

            var archive = await ReadArchiveAsync(sdist, "my_package-1.0.tar.gz", "my-package");

            Assert.Equal("my-package", PyPICoreMetadata.GetString(archive.Manifest, "name"));
        }

        [Fact]
        public async Task ReadsThePkgInfoOfAZippedSdist()
        {
            using var sdist = Zip(("my-package-1.0/PKG-INFO", Metadata("my-package", "1.0")));

            var archive = await ReadArchiveAsync(sdist, "my-package-1.0.zip", "my-package");

            Assert.Equal("1.0", PyPICoreMetadata.GetString(archive.Manifest, "version"));
        }

        [Fact]
        public async Task RejectsAWheelWithoutMetadata()
        {
            using var wheel = Zip(("my_package/__init__.py", ""));

            var e = await Assert.ThrowsAsync<InvalidPyPIPackageException>(() => ReadArchiveAsync(wheel, "my_package-1.0-py3-none-any.whl", "my-package"));

            Assert.Contains("METADATA", e.Message);
        }

        [Fact]
        public async Task RejectsAnSdistWithoutPkgInfo()
        {
            using var sdist = TarGz(("my_package-1.0/setup.py", ""));

            var e = await Assert.ThrowsAsync<InvalidPyPIPackageException>(() => ReadArchiveAsync(sdist, "my_package-1.0.tar.gz", "my-package"));

            Assert.Contains("PKG-INFO", e.Message);
        }

        [Theory]
        [InlineData("other", "1.0", "is for the project 'other'")]
        [InlineData("my-package", "2.0", "is for version '2.0'")]
        public async Task RejectsMetadataOfAnotherProjectOrVersion(string name, string version, string message)
        {
            using var wheel = Zip(("my_package-1.0.dist-info/METADATA", Metadata(name, version)));

            var e = await Assert.ThrowsAsync<InvalidPyPIPackageException>(() => ReadArchiveAsync(wheel, "my_package-1.0-py3-none-any.whl", "my-package"));

            Assert.Contains(message, e.Message);
        }

        [Theory]
        [InlineData("my_package-1.0-py3-none-any.whl")]
        [InlineData("my_package-1.0.tar.gz")]
        public async Task RejectsAFileThatIsNotAnArchive(string fileName)
        {
            using var content = new MemoryStream(Encoding.UTF8.GetBytes("not an archive"));

            var e = await Assert.ThrowsAsync<InvalidPyPIPackageException>(() => ReadArchiveAsync(content, fileName, "my-package"));

            Assert.Contains("not a valid", e.Message);
        }

        private static async Task<PyPIArchive> ReadArchiveAsync(Stream content, string fileName, string project)
        {
            Assert.True(PyPIFileName.TryParse(fileName, project, out var file));

            return await PyPIArchiveReader.ReadAsync(content, file, project, CancellationToken.None);
        }

        private static string Metadata(string name, string version)
        {
            return $"Metadata-Version: 2.1\nName: {name}\nVersion: {version}\nSummary: A test package.\n\nThe readme.\n";
        }

        private static MemoryStream Zip(params (string Path, string Content)[] entries)
        {
            var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (path, content) in entries)
                {
                    using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                    writer.Write(content);
                }
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream TarGz(params (string Path, string Content)[] entries)
        {
            var stream = new MemoryStream();
            using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true))
            using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
            {
                foreach (var (path, content) in entries)
                {
                    tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, path)
                    {
                        DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
                    });
                }
            }

            stream.Position = 0;
            return stream;
        }
    }
}
