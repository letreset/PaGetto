using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Npm;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Npm;

public class NpmArchiveReaderTests
{
    [Fact]
    public async Task ReadsTheManifestAndReadme()
    {
        var tarball = NpmTarballBuilder.Create("@scope/name", "1.2.3-beta.1", readme: "# Hello");

        var archive = await NpmArchiveReader.ReadAsync(new MemoryStream(tarball), CancellationToken.None);

        Assert.Equal("@scope/name", archive.Name);
        Assert.Equal("1.2.3-beta.1", archive.Version.ToString());
        Assert.Equal("A test package.", archive.Manifest["description"]?.GetValue<string>());
        Assert.Equal("# Hello", archive.Readme);
    }

    [Fact]
    public async Task AcceptsAnyFolderNameAndReadmeSpelling()
    {
        // Older packages were packed with the package's own folder name.
        using var tarball = PubArchiveBuilder.Create(new Dictionary<string, string>
        {
            ["node-thing/package.json"] = NpmTarballBuilder.CreateManifest("thing", "0.1.0").ToJsonString(),
            ["node-thing/Readme.markdown"] = "Old readme",
            ["node-thing/lib/README.md"] = "Not the package's readme",
        });

        var archive = await NpmArchiveReader.ReadAsync(tarball, CancellationToken.None);

        Assert.Equal("thing", archive.Name);
        Assert.Equal("Old readme", archive.Readme);
    }

    [Fact]
    public async Task IgnoresPackageJsonFilesOfSubfolders()
    {
        using var tarball = PubArchiveBuilder.Create(new Dictionary<string, string>
        {
            ["package/node_modules/dep/package.json"] = NpmTarballBuilder.CreateManifest("dep", "1.0.0").ToJsonString(),
        });

        var e = await Assert.ThrowsAsync<InvalidNpmPackageException>(() => NpmArchiveReader.ReadAsync(tarball, CancellationToken.None));

        Assert.Contains("package.json", e.Message);
    }

    [Fact]
    public async Task RejectsDataThatIsNotGzip()
    {
        var e = await Assert.ThrowsAsync<InvalidNpmPackageException>(
            () => NpmArchiveReader.ReadAsync(new MemoryStream([1, 2, 3, 4]), CancellationToken.None));

        Assert.Contains("gzipped tar", e.Message);
    }

    [Theory]
    [InlineData("Not A Name", "1.0.0", "name")]
    [InlineData("name", "1.0", "version")]
    public async Task RejectsAnInvalidNameOrVersion(string name, string version, string expected)
    {
        var tarball = NpmTarballBuilder.Create(new JsonObject { ["name"] = name, ["version"] = version });

        var e = await Assert.ThrowsAsync<InvalidNpmPackageException>(
            () => NpmArchiveReader.ReadAsync(new MemoryStream(tarball), CancellationToken.None));

        Assert.Contains(expected, e.Message);
    }

    [Fact]
    public async Task RejectsAManifestThatIsNotJson()
    {
        using var tarball = PubArchiveBuilder.Create(new Dictionary<string, string> { ["package/package.json"] = "{ nope" });

        var e = await Assert.ThrowsAsync<InvalidNpmPackageException>(() => NpmArchiveReader.ReadAsync(tarball, CancellationToken.None));

        Assert.Contains("not valid JSON", e.Message);
    }
}
