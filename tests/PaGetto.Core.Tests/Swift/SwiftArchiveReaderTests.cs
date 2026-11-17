using System.Collections.Generic;
using System.IO;
using PaGetto.Core.Swift;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Swift;

public class SwiftArchiveReaderTests
{
    public class Read
    {
        [Fact]
        public void ReadsTheManifestsAndReadmeOfThePackageFolder()
        {
            var archive = ReadZip(SwiftArchiveBuilder.Create(readme: "# LinkedList", extraFiles: new Dictionary<string, string>
            {
                ["Package@swift-5.7.swift"] = "// swift-tools-version:5.7\n",
                ["Sources/LinkedList/Package.swift"] = "not a manifest",
                ["Package@swift-five.swift"] = "not a manifest either",
            }));

            Assert.Equal(new[] { "Package.swift", "Package@swift-5.7.swift" }, archive.Manifests.Keys);
            Assert.Equal(SwiftArchiveBuilder.PackageSwift(), archive.Manifests["Package.swift"]);
            Assert.Equal("# LinkedList", archive.Readme);
        }

        [Fact]
        public void ReadsAReadmeOfAnyCase()
        {
            var archive = ReadZip(SwiftArchiveBuilder.Create(extraFiles: new Dictionary<string, string> { ["readme.md"] = "lowercase" }));

            Assert.Equal("lowercase", archive.Readme);
        }

        [Fact]
        public void PackageWithoutAReadmeHasNone()
        {
            Assert.Null(ReadZip(SwiftArchiveBuilder.Create()).Readme);
        }

        [Fact]
        public void PackageAtTheRootOfTheZipIsRefused()
        {
            // Swift Package Manager strips the top-level folder, so the package would lose a level.
            var zip = SwiftArchiveBuilder.CreateZip(new Dictionary<string, string> { ["Package.swift"] = SwiftArchiveBuilder.PackageSwift() });

            var e = Assert.Throws<InvalidSwiftPackageException>(() => ReadZip(zip));
            Assert.Contains("one folder at its top level", e.Message);
        }

        [Fact]
        public void ZipWithTwoFoldersIsRefused()
        {
            var zip = SwiftArchiveBuilder.CreateZip(new Dictionary<string, string>
            {
                ["LinkedList/Package.swift"] = SwiftArchiveBuilder.PackageSwift(),
                ["__MACOSX/LinkedList/._Package.swift"] = "",
            });

            var e = Assert.Throws<InvalidSwiftPackageException>(() => ReadZip(zip));
            Assert.Contains("but it has 2", e.Message);
        }

        [Fact]
        public void FolderWithoutAManifestIsRefused()
        {
            var zip = SwiftArchiveBuilder.CreateZip(new Dictionary<string, string> { ["LinkedList/Sources/A.swift"] = "" });

            var e = Assert.Throws<InvalidSwiftPackageException>(() => ReadZip(zip));
            Assert.Equal("The source archive has no Package.swift in its folder LinkedList/.", e.Message);
        }

        [Fact]
        public void AnythingButAZipIsRefused()
        {
            var e = Assert.Throws<InvalidSwiftPackageException>(() => ReadZip([1, 2, 3]));
            Assert.Equal("The source archive is not a valid zip file.", e.Message);
        }

        private static SwiftArchive ReadZip(byte[] zip)
        {
            using var stream = new MemoryStream(zip);
            return SwiftArchiveReader.Read(stream);
        }
    }
}
