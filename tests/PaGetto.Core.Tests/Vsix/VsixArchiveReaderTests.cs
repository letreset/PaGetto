using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PaGetto.Core.Tests.Support;
using PaGetto.Core.Vsix;
using Xunit;

namespace PaGetto.Core.Tests.Vsix;

public class VsixArchiveReaderTests
{
    public class Read
    {
        [Fact]
        public void ReadsTheManifest()
        {
            var manifest = VsixArchiveReader.Read(new MemoryStream(VsixPackageBuilder.Create("Contoso.HelloTools", "1.02.0")));

            Assert.Equal("Contoso.HelloTools", manifest.Id);
            Assert.Equal("1.2.0", manifest.Version);
            Assert.Equal("Contoso", manifest.Publisher);
            Assert.Equal("en-US", manifest.Language);
            Assert.Equal("Hello Tools", manifest.DisplayName);
            Assert.Equal("Says hello from the editor.", manifest.Description);
            Assert.Equal("https://github.com/contoso/hello-tools", manifest.MoreInfo);
            Assert.Equal("ReleaseNotes.txt", manifest.ReleaseNotes);
            Assert.Equal(@"Resources\Icon.png", manifest.Icon);
            Assert.Equal(@"Resources\Preview.png", manifest.PreviewImage);
            Assert.Equal(new[] { "hello", "greeting", "editor" }, manifest.Tags);
            Assert.False(manifest.Preview);

            Assert.Equal(2, manifest.InstallationTargets.Count);
            Assert.All(manifest.InstallationTargets, t => Assert.Equal("Microsoft.VisualStudio.Community", t.Id));
            Assert.All(manifest.InstallationTargets, t => Assert.Equal("[17.0, 18.0)", t.Version));
            Assert.Equal(new[] { "amd64", "arm64" }, manifest.InstallationTargets.Select(t => t.ProductArchitecture));

            // The .NET Framework is not an extension.
            var dependency = Assert.Single(manifest.Dependencies);
            Assert.Equal("Contoso.Shared", dependency.Id);
            Assert.Equal("Contoso Shared", dependency.DisplayName);
            Assert.Equal("[1.0,)", dependency.Version);
        }

        [Fact]
        public void LeavesOutImagesTheFileDoesNotHaveOrThatAreNotServed()
        {
            var manifest = VsixPackageBuilder.Manifest("Hello", "1.0")
                .Replace(@"<Icon>Resources\Icon.png</Icon>", "<Icon>Resources/Missing.png</Icon>")
                .Replace(@"<PreviewImage>Resources\Preview.png</PreviewImage>", "<PreviewImage>Resources/Preview.svg</PreviewImage>");
            var files = new Dictionary<string, byte[]> { ["Resources/Preview.svg"] = Encoding.UTF8.GetBytes("<svg />") };

            var read = VsixArchiveReader.Read(new MemoryStream(VsixPackageBuilder.Create("Hello", "1.0", manifest, extraFiles: files)));

            Assert.Null(read.Icon);
            Assert.Null(read.PreviewImage);
        }

        [Fact]
        public void ReadsThePreviewFlag()
        {
            var manifest = VsixPackageBuilder.Manifest("Hello", "1.0", extraMetadata: "<Preview>true</Preview>");

            Assert.True(VsixArchiveReader.Read(new MemoryStream(VsixPackageBuilder.Create("Hello", "1.0", manifest))).Preview);
        }

        public static IEnumerable<object[]> InvalidFiles()
        {
            yield return [new byte[] { 1, 2, 3 }, "not a valid VSIX (zip) file"];
            yield return [VsixPackageBuilder.Create("Hello", "1.0", contentTypes: false), "has no [Content_Types].xml"];
            yield return [VsixPackageBuilder.Create("Hello", "1.0", "<PackageManifest"), "not valid XML"];
            yield return [VsixPackageBuilder.Create("Hello", "1.0", "<Vsix Version=\"1.0.0\" xmlns=\"http://schemas.microsoft.com/developer/vsx-schema/2010\" />"), "format of Visual Studio 2010"];
            yield return [VsixPackageBuilder.Create("Hello", "1.0", "<Other />"), "not a VSIX manifest"];
            yield return [VsixPackageBuilder.Create("with space", "1.0"), "The Id with space of the manifest is not valid"];
            yield return [VsixPackageBuilder.Create("Hello", "1.0.0-beta"), "The Version 1.0.0-beta of the manifest is not valid"];
            yield return [VsixPackageBuilder.Create("Hello", "1.0", VsixPackageBuilder.Manifest("Hello", "1.0").Replace("<DisplayName>Hello Tools</DisplayName>", "")), "no DisplayName"];
            yield return [VsixPackageBuilder.Create("Hello", "1.0", VsixPackageBuilder.Manifest("Hello", "1.0", installation: " ")), "no Installation/InstallationTarget"];
            yield return
            [
                VsixPackageBuilder.Create("Hello", "1.0", VsixPackageBuilder.Manifest("Hello", "1.0", installation: "<InstallationTarget Id=\"Microsoft.VisualStudio.Code\" />")),
                "Hello is a VS Code extension",
            ];
        }

        [Theory]
        [MemberData(nameof(InvalidFiles))]
        public void RejectsInvalidFilesWithAMessage(byte[] vsix, string message)
        {
            var exception = Assert.Throws<InvalidVsixPackageException>(() => VsixArchiveReader.Read(new MemoryStream(vsix)));

            Assert.Contains(message, exception.Message);
        }

        [Fact]
        public void RejectsAManifestWithADocumentTypeDefinition()
        {
            var body = VsixPackageBuilder.Manifest("Hello", "1.0");
            var manifest = "<!DOCTYPE x [<!ENTITY e \"e\">]>" + body[body.IndexOf("<PackageManifest", System.StringComparison.Ordinal)..];

            var exception = Assert.Throws<InvalidVsixPackageException>(() => VsixArchiveReader.Read(new MemoryStream(VsixPackageBuilder.Create("Hello", "1.0", manifest))));

            Assert.Contains("not valid XML", exception.Message);
        }
    }

    public class ReadImage
    {
        [Fact]
        public void ReadsAnImageByItsPathInTheManifest()
        {
            var vsix = new MemoryStream(VsixPackageBuilder.Create("Hello", "1.0"));

            var image = VsixArchiveReader.ReadImage(vsix, @"Resources\Icon.png");

            Assert.Equal(VsixPackageBuilder.Icon, image.Content);
            Assert.Equal("image/png", image.ContentType);
        }

        [Fact]
        public void FindsAnEscapedPartName()
        {
            var files = new Dictionary<string, byte[]> { ["Resources/My%20Icon.png"] = VsixPackageBuilder.Icon };
            var vsix = new MemoryStream(VsixPackageBuilder.Create("Hello", "1.0", extraFiles: files));

            Assert.Equal(VsixPackageBuilder.Icon, VsixArchiveReader.ReadImage(vsix, "Resources/My Icon.png").Content);
        }

        [Theory]
        [InlineData("Resources/Missing.png")]
        [InlineData("HelloTools.dll")]
        public void ReturnsNullForAMissingFileOrOneThatIsNotAnImage(string path)
        {
            Assert.Null(VsixArchiveReader.ReadImage(new MemoryStream(VsixPackageBuilder.Create("Hello", "1.0")), path));
        }
    }
}
