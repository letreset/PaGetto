using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Go;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Go;

public class GoModuleZipReaderTests
{
    private const string Module = "example.com/Tool";
    private const string Version = "v1.0.0";
    private const string Folder = Module + "@" + Version + "/";

    public class Read
    {
        [Fact]
        public void ReadsTheGoModAndReadme()
        {
            var zip = GoModuleZipBuilder.Create(Module, Version, readme: "# Tool");

            var module = ReadZip(zip);

            Assert.Equal(GoModuleZipBuilder.GoMod(Module), module.GoMod);
            Assert.Equal("# Tool", module.Readme);
            Assert.False(module.HasDirectoryEntries);
        }

        [Fact]
        public void LeavesOutGoModFilesOfSubfolders()
        {
            var module = ReadZip(GoModuleZipBuilder.CreateZip(new Dictionary<string, string>
            {
                [Folder] = "",
                [Folder + "nested/"] = "",
                [Folder + "nested/go.mod"] = "module example.com/Tool/nested\n",
                [Folder + "a.go"] = "package a\n",
            }));

            Assert.Null(module.GoMod);
            Assert.Null(module.Readme);
            Assert.True(module.HasDirectoryEntries);
        }

        [Theory]
        [InlineData("example.com/tool@v1.0.0/go.mod", "not in the folder")]
        [InlineData("example.com/Tool@v1.0.1/go.mod", "not in the folder")]
        [InlineData("go.mod", "not in the folder")]
        [InlineData(Folder + "../go.mod", "invalid path")]
        [InlineData(Folder + "a//b.go", "invalid path")]
        [InlineData(Folder + "a\\b.go", "invalid path")]
        [InlineData(Folder + "GO.MOD", "must be named go.mod")]
        public void RejectsFilesOutsideTheModuleFolder(string fileName, string message)
        {
            var zip = GoModuleZipBuilder.CreateZip(new Dictionary<string, string> { [fileName] = "module example.com/Tool\n" });

            var e = Assert.Throws<InvalidGoModuleException>(() => ReadZip(zip));

            Assert.Contains(message, e.Message);
        }

        [Fact]
        public void RejectsFilesThatDifferOnlyInCase()
        {
            var zip = GoModuleZipBuilder.CreateZip(new Dictionary<string, string>
            {
                [Folder + "go.mod"] = "module example.com/Tool\n",
                [Folder + "a.go"] = "package a\n",
                [Folder + "A.go"] = "package a\n",
            });

            var e = Assert.Throws<InvalidGoModuleException>(() => ReadZip(zip));

            Assert.Contains("twice", e.Message);
        }

        [Fact]
        public void RejectsDataThatIsNotAZip()
        {
            var e = Assert.Throws<InvalidGoModuleException>(() => ReadZip([1, 2, 3, 4]));

            Assert.Contains("not a valid zip", e.Message);
        }

        private static GoModuleZip ReadZip(byte[] zip)
        {
            using var stream = new MemoryStream(zip);
            return GoModuleZipReader.Read(stream, Module, Version);
        }
    }

    public class RemoveDirectoryEntriesAsync
    {
        [Fact]
        public async Task KeepsOnlyTheFiles()
        {
            using var zip = new MemoryStream(GoModuleZipBuilder.CreateZip(new Dictionary<string, string>
            {
                [Folder] = "",
                [Folder + "go.mod"] = "module example.com/Tool\n",
                [Folder + "sub/"] = "",
                [Folder + "sub/a.go"] = "package sub\n",
            }));

            using var result = await GoModuleZipReader.RemoveDirectoryEntriesAsync(zip, CancellationToken.None);

            using var archive = new ZipArchive(result.Stream, ZipArchiveMode.Read, leaveOpen: true);
            Assert.Equal(new[] { Folder + "go.mod", Folder + "sub/a.go" }, archive.Entries.Select(e => e.FullName));
            using var reader = new StreamReader(archive.GetEntry(Folder + "sub/a.go")!.Open());
            Assert.Equal("package sub\n", reader.ReadToEnd());
        }
    }
}
