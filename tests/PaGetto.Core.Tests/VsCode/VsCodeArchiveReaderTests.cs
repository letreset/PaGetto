using System.IO;
using System.Text;
using PaGetto.Core.Tests.Support;
using PaGetto.Core.VsCode;
using Xunit;

namespace PaGetto.Core.Tests.VsCode;

public class VsCodeArchiveReaderTests
{
    public class Read
    {
        [Fact]
        public void ReadsTheManifest()
        {
            var manifest = VsCodeArchiveReader.Read(new MemoryStream(VsCodePackageBuilder.Create("Contoso", "hello-world", "1.2.3")));

            Assert.Equal("Contoso", manifest.Publisher);
            Assert.Equal("hello-world", manifest.Name);
            Assert.Equal("Contoso.hello-world", manifest.Id);
            Assert.Equal("1.2.3", manifest.Version);
            Assert.Equal(VsCodeTargetPlatform.Universal, manifest.TargetPlatform);
            Assert.Equal("Hello World", manifest.DisplayName);
            Assert.Equal("Says hello from the editor.", manifest.Description);
            Assert.Equal(["hello", "greeting", "__ext_hello"], manifest.Tags);
            Assert.Equal(["Other", "Snippets"], manifest.Categories);
            Assert.Equal("Public", manifest.GalleryFlags);
            Assert.Equal("^1.90.0", manifest.GetProperty(VsCodeGalleryApi.EngineProperty));
            Assert.Null(manifest.GetProperty(VsCodeGalleryApi.ExtensionPackProperty));
            Assert.False(manifest.IsPreRelease);
            Assert.Equal("extension/package.json", manifest.Assets[VsCodeGalleryApi.ManifestAsset]);
            Assert.Equal("extension/README.md", manifest.Assets[VsCodeGalleryApi.DetailsAsset]);
            Assert.Equal("extension/icon.png", manifest.Assets[VsCodeGalleryApi.IconAsset]);
        }

        [Fact]
        public void ReadsThePlatformAndPreRelease()
        {
            var vsix = VsCodePackageBuilder.Create(
                "contoso",
                "hello",
                "2.0.0",
                manifest: VsCodePackageBuilder.Manifest(
                    "contoso",
                    "hello",
                    "2.0.0",
                    "Linux-X64",
                    """<Property Id="Microsoft.VisualStudio.Code.PreRelease" Value="true" />"""));

            var manifest = VsCodeArchiveReader.Read(new MemoryStream(vsix));

            Assert.Equal("linux-x64", manifest.TargetPlatform);
            Assert.True(manifest.IsPreRelease);
        }

        [Fact]
        public void LeavesOutAssetsTheFileDoesNotHave()
        {
            var manifest = VsCodePackageBuilder.Manifest("contoso", "hello", "1.0.0")
                .Replace("</Assets>", """<Asset Type="Microsoft.VisualStudio.Services.Content.License" Path="extension/LICENSE.txt" Addressable="true" /></Assets>""");

            var read = VsCodeArchiveReader.Read(new MemoryStream(VsCodePackageBuilder.Create("contoso", "hello", "1.0.0", manifest: manifest)));

            Assert.False(read.Assets.ContainsKey(VsCodeGalleryApi.LicenseAsset));
        }

        [Theory]
        [InlineData("Microsoft.VisualStudio.Community", "does not target Microsoft.VisualStudio.Code")]
        public void RefusesVisualStudioExtensions(string target, string message)
        {
            var manifest = VsCodePackageBuilder.Manifest("contoso", "hello", "1.0.0", installationTarget: target);

            var e = Assert.Throws<InvalidVsCodePackageException>(() =>
                VsCodeArchiveReader.Read(new MemoryStream(VsCodePackageBuilder.Create("contoso", "hello", "1.0.0", manifest: manifest))));

            Assert.Contains(message, e.Message);
        }

        [Theory]
        [InlineData("contoso", "hello", "1.0", "not a semantic version")]
        [InlineData("contoso", "hello", "latest", "not a semantic version")]
        [InlineData("con toso", "hello", "1.0.0", "no valid identifier")]
        [InlineData("contoso", "-hello", "1.0.0", "no valid identifier")]
        [InlineData("contoso", "he/llo", "1.0.0", "no valid identifier")]
        public void RefusesInvalidIdentities(string publisher, string name, string version, string message)
        {
            var vsix = VsCodePackageBuilder.Create(publisher, name, version);

            var e = Assert.Throws<InvalidVsCodePackageException>(() => VsCodeArchiveReader.Read(new MemoryStream(vsix)));

            Assert.Contains(message, e.Message);
        }

        [Fact]
        public void RefusesAnUnknownPlatform()
        {
            var vsix = VsCodePackageBuilder.Create("contoso", "hello", "1.0.0", targetPlatform: "amiga-m68k");

            var e = Assert.Throws<InvalidVsCodePackageException>(() => VsCodeArchiveReader.Read(new MemoryStream(vsix)));

            Assert.Contains("amiga-m68k", e.Message);
        }

        [Fact]
        public void RefusesAFileWithoutPackageJson()
        {
            var vsix = VsCodePackageBuilder.Create("contoso", "hello", "1.0.0", packageJson: false);

            var e = Assert.Throws<InvalidVsCodePackageException>(() => VsCodeArchiveReader.Read(new MemoryStream(vsix)));

            Assert.Contains("package.json", e.Message);
        }

        [Fact]
        public void RefusesAFileThatIsNotAZip()
        {
            var e = Assert.Throws<InvalidVsCodePackageException>(() => VsCodeArchiveReader.Read(new MemoryStream(Encoding.UTF8.GetBytes("not a zip"))));

            Assert.Contains("not a valid VSIX", e.Message);
        }
    }

    public class ReadFileOrNull
    {
        [Fact]
        public void ReadsAFileOrNull()
        {
            var vsix = new MemoryStream(VsCodePackageBuilder.Create("contoso", "hello", "1.0.0"));

            Assert.Equal(VsCodePackageBuilder.Readme, Encoding.UTF8.GetString(VsCodeArchiveReader.ReadFileOrNull(vsix, "extension/readme.md")));
            Assert.Null(VsCodeArchiveReader.ReadFileOrNull(vsix, "extension/missing.md"));
        }
    }
}
