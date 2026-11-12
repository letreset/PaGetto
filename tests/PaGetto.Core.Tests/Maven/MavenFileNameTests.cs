using PaGetto.Core.Maven;
using Xunit;

namespace PaGetto.Core.Tests.Maven;

public class MavenFileNameTests
{
    public class TryParse
    {
        [Theory]
        [InlineData("my-lib-1.0.jar", null, "jar")]
        [InlineData("my-lib-1.0.pom", null, "pom")]
        [InlineData("my-lib-1.0-sources.jar", "sources", "jar")]
        [InlineData("my-lib-1.0-linux-x86_64.zip", "linux-x86_64", "zip")]
        [InlineData("my-lib-1.0.jar.asc", null, "jar.asc")]
        [InlineData("my-lib-1.0.tar.gz", null, "tar.gz")]
        public void ReadsTheClassifierAndExtension(string fileName, string classifier, string extension)
        {
            Assert.True(MavenFileName.TryParse("my-lib", "1.0", fileName, out var file));

            Assert.Equal("1.0", file.FileVersion);
            Assert.Equal(classifier, file.Classifier);
            Assert.Equal(extension, file.Extension);
            Assert.Null(file.SnapshotTimestamp);
            Assert.Equal(0, file.SnapshotBuildNumber);
        }

        [Fact]
        public void ReadsTheBuildOfATimestampedSnapshotFile()
        {
            Assert.True(MavenFileName.TryParse("my-lib", "1.0-SNAPSHOT", "my-lib-1.0-20240102.030405-12-javadoc.jar", out var file));

            Assert.Equal("1.0-20240102.030405-12", file.FileVersion);
            Assert.Equal("20240102.030405", file.SnapshotTimestamp);
            Assert.Equal(12, file.SnapshotBuildNumber);
            Assert.Equal("javadoc", file.Classifier);
            Assert.Equal("jar", file.Extension);
        }

        [Fact]
        public void ReadsANonUniqueSnapshotFile()
        {
            Assert.True(MavenFileName.TryParse("my-lib", "1.0-SNAPSHOT", "my-lib-1.0-SNAPSHOT.pom", out var file));

            Assert.Equal("1.0-SNAPSHOT", file.FileVersion);
            Assert.Null(file.SnapshotTimestamp);
            Assert.True(file.IsPom);
        }

        [Theory]
        [InlineData("my-lib-1.0.pom", true, false)]
        [InlineData("my-lib-1.0-tests.pom", false, false)]
        [InlineData("my-lib-1.0.pom.asc", false, true)]
        [InlineData("my-lib-1.0.jar", false, false)]
        public void KnowsThePomAndSignatures(string fileName, bool isPom, bool isSignature)
        {
            Assert.True(MavenFileName.TryParse("my-lib", "1.0", fileName, out var file));

            Assert.Equal(isPom, file.IsPom);
            Assert.Equal(isSignature, file.IsSignature);
        }

        [Theory]
        [InlineData("my-lib-1.0")]
        [InlineData("my-lib-1.0.")]
        [InlineData("my-lib-1.0-.jar")]
        [InlineData("my-lib-1.0-sources")]
        [InlineData("my-lib-1.0x.jar")]
        [InlineData("other-1.0.jar")]
        [InlineData("my-lib-2.0.jar")]
        public void RejectsNamesOfOtherFiles(string fileName)
        {
            Assert.False(MavenFileName.TryParse("my-lib", "1.0", fileName, out _));
        }

        [Fact]
        public void RejectsAMalformedSnapshotBuild()
        {
            Assert.False(MavenFileName.TryParse("my-lib", "1.0-SNAPSHOT", "my-lib-1.0-2024.jar", out _));
        }
    }
}
