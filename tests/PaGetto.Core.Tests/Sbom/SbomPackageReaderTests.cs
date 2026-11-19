using System;
using System.Linq;
using PaGetto.Core.Entities;
using PaGetto.Core.Sbom;
using Xunit;

namespace PaGetto.Core.Tests.Sbom;

public class SbomPackageReaderTests
{
    public class Read
    {
        public static TheoryData<FeedType> FeedTypes => new(Enum.GetValues<FeedType>());

        [Theory]
        [MemberData(nameof(FeedTypes))]
        public void ReadsTheManifestOfEveryFeedType(FeedType feedType)
        {
            var sample = SbomSamples.Get(feedType);

            var package = SbomSamples.Read(sample);

            Assert.Equal(feedType, package.FeedType);
            Assert.Equal(sample.ExpectedName, package.Name);
            Assert.Equal(sample.ExpectedLicense, package.Licenses.SingleOrDefault());
            Assert.Equal(sample.ExpectedDependencies, package.Dependencies);
            Assert.Equal(sample.Sha256, package.Sha256);
        }

        [Fact]
        public void LeavesOutTheCoreMetadataOfAWheel()
        {
            var package = SbomSamples.Read(SbomSamples.Get(FeedType.PyPI));

            Assert.Equal("my_package-1.0-py3-none-any.whl", Assert.Single(package.Files).FileName);
        }

        [Fact]
        public void ReadsEveryLicenseOfAHexPackage()
        {
            var version = new ArtifactVersion
            {
                Artifact = new Artifact { Name = "plug" },
                Version = "1.0.0",
                MetadataJson = """{"name": "plug", "licenses": ["Apache-2.0", "MIT"]}""",
            };

            Assert.Equal(["Apache-2.0", "MIT"], SbomPackageReader.Read(FeedType.Hex, version, []).Licenses);
        }

        [Fact]
        public void FallsBackToTheStoredNameWithoutAManifest()
        {
            var version = new ArtifactVersion { Artifact = new Artifact { Name = "thing" }, Version = "1.0.0", MetadataJson = "not json" };

            var package = SbomPackageReader.Read(FeedType.Npm, version, []);

            Assert.Equal("thing", package.Name);
            Assert.Empty(package.Licenses);
            Assert.Empty(package.Dependencies);
        }
    }
}
