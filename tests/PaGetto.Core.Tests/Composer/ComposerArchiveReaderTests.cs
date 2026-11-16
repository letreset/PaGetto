using System.Collections.Generic;
using System.IO;
using PaGetto.Core.Composer;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Composer;

public class ComposerArchiveReaderTests
{
    public class Read
    {
        [Fact]
        public void ReadsComposerJsonAndReadmeAtTheRoot()
        {
            var archive = ReadZip(ComposerZipBuilder.Create("acme/hello", readme: "# Hello"));

            Assert.Equal("acme/hello", archive.ComposerJson["name"]!.GetValue<string>());
            Assert.Equal("# Hello", archive.Readme);
        }

        [Fact]
        public void ReadsThePackageInTheOnlyFolderOfTheZip()
        {
            // As GitHub and git archive --prefix write them.
            var archive = ReadZip(ComposerZipBuilder.Create("acme/hello", folder: "acme-hello-0a1b2c3", readme: "# Hello"));

            Assert.Equal("acme/hello", archive.ComposerJson["name"]!.GetValue<string>());
            Assert.Equal("# Hello", archive.Readme);
        }

        [Fact]
        public void ReadsAReadmeOfAnyCase()
        {
            var zip = ComposerZipBuilder.CreateZip(new Dictionary<string, string>
            {
                ["composer.json"] = ComposerZipBuilder.ComposerJson("acme/hello"),
                ["readme.md"] = "lowercase",
            });

            Assert.Equal("lowercase", ReadZip(zip).Readme);
        }

        [Fact]
        public void PackageWithoutAReadmeHasNone()
        {
            Assert.Null(ReadZip(ComposerZipBuilder.Create("acme/hello")).Readme);
        }

        [Fact]
        public void ComposerJsonInAFolderNextToOtherFilesIsNotFound()
        {
            // Composer only installs from the folder when it is alone at the top of the zip.
            var zip = ComposerZipBuilder.CreateZip(new Dictionary<string, string>
            {
                ["hello/composer.json"] = ComposerZipBuilder.ComposerJson("acme/hello"),
                ["LICENSE"] = "MIT",
            });

            var e = Assert.Throws<InvalidComposerPackageException>(() => ReadZip(zip));
            Assert.Contains("no composer.json at its root", e.Message);
        }

        [Fact]
        public void ComposerJsonInASubfolderIsNotFound()
        {
            var zip = ComposerZipBuilder.CreateZip(new Dictionary<string, string>
            {
                ["hello/sub/composer.json"] = ComposerZipBuilder.ComposerJson("acme/hello"),
            });

            var e = Assert.Throws<InvalidComposerPackageException>(() => ReadZip(zip));
            Assert.Contains("or in its folder hello/", e.Message);
        }

        [Theory]
        [InlineData("{ not json", "not valid JSON")]
        [InlineData("[1, 2]", "not a JSON object")]
        public void InvalidComposerJsonIsRejected(string composerJson, string message)
        {
            var e = Assert.Throws<InvalidComposerPackageException>(() => ReadZip(ComposerZipBuilder.Create("acme/hello", composerJson: composerJson)));

            Assert.Contains(message, e.Message);
        }

        [Fact]
        public void SomethingElseThanAZipIsRejected()
        {
            var e = Assert.Throws<InvalidComposerPackageException>(() => ReadZip([1, 2, 3]));

            Assert.Contains("not a valid zip", e.Message);
        }

        private static ComposerArchive ReadZip(byte[] zip)
        {
            using var stream = new MemoryStream(zip);
            return ComposerArchiveReader.Read(stream);
        }
    }
}
