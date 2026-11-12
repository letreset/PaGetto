using PaGetto.Core.Maven;
using Xunit;

namespace PaGetto.Core.Tests.Maven;

public class MavenPathTests
{
    public class TryParse
    {
        [Fact]
        public void ReadsAFileOfAVersion()
        {
            Assert.True(MavenPath.TryParse("com/example/my-lib/1.0/my-lib-1.0-sources.jar", out var path));

            Assert.Equal(MavenPathKind.File, path.Kind);
            Assert.Equal("com.example", path.GroupId);
            Assert.Equal("my-lib", path.ArtifactId);
            Assert.Equal("1.0", path.Version);
            Assert.Equal("my-lib-1.0-sources.jar", path.FileName);
            Assert.Equal("sources", path.File.Classifier);
            Assert.Equal("jar", path.File.Extension);
            Assert.Null(path.Checksum);
            Assert.Equal("com.example:my-lib", path.PackageName);
        }

        [Theory]
        [InlineData("my-lib-1.0.jar.sha1", MavenChecksum.Sha1, "my-lib-1.0.jar")]
        [InlineData("my-lib-1.0.pom.md5", MavenChecksum.Md5, "my-lib-1.0.pom")]
        [InlineData("my-lib-1.0.module.sha256", MavenChecksum.Sha256, "my-lib-1.0.module")]
        [InlineData("my-lib-1.0.jar.asc.sha512", MavenChecksum.Sha512, "my-lib-1.0.jar.asc")]
        public void ReadsTheChecksumOfAFile(string fileName, MavenChecksum checksum, string checkedFileName)
        {
            Assert.True(MavenPath.TryParse($"com/example/my-lib/1.0/{fileName}", out var path));

            Assert.Equal(MavenPathKind.File, path.Kind);
            Assert.Equal(checksum, path.Checksum);
            Assert.Equal(checkedFileName, path.FileName);
            Assert.Equal($"com/example/my-lib/1.0/{checkedFileName}", path.FilePath);
        }

        [Fact]
        public void ReadsTheMetadataOfAnArtifact()
        {
            Assert.True(MavenPath.TryParse("org/example/My-Lib/maven-metadata.xml.sha1", out var path));

            Assert.Equal(MavenPathKind.ArtifactMetadata, path.Kind);
            Assert.Equal("org.example", path.GroupId);
            Assert.Equal("My-Lib", path.ArtifactId);
            Assert.Null(path.Version);
            Assert.Equal(MavenChecksum.Sha1, path.Checksum);
            Assert.Equal("org/example/My-Lib/maven-metadata.xml", path.FilePath);
            Assert.Equal("org.example:my-lib", path.PackageName);
        }

        [Fact]
        public void ReadsTheMetadataOfASnapshot()
        {
            Assert.True(MavenPath.TryParse("org/example/my-lib/1.0-SNAPSHOT/maven-metadata.xml", out var path));

            Assert.Equal(MavenPathKind.SnapshotMetadata, path.Kind);
            Assert.Equal("my-lib", path.ArtifactId);
            Assert.Equal("1.0-SNAPSHOT", path.Version);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("my-lib-1.0.jar")]
        [InlineData("my-lib/1.0/my-lib-1.0.jar")]
        [InlineData("com/example/my-lib/1.0/other-1.0.jar")]
        [InlineData("com/example/my-lib/1.0/my-lib-2.0.jar")]
        [InlineData("com/example/my-lib/1.0/my-lib-1.0")]
        [InlineData("com/example/my-lib/1.0/")]
        [InlineData("com//my-lib/1.0/my-lib-1.0.jar")]
        [InlineData("com/../my-lib/1.0/my-lib-1.0.jar")]
        [InlineData("com/./my-lib/1.0/my-lib-1.0.jar")]
        [InlineData("com/ex%2Fample/my-lib/1.0/my-lib-1.0.jar")]
        [InlineData("com/ex ample/my-lib/1.0/my-lib-1.0.jar")]
        [InlineData("com/example.org/my-lib/1.0/my-lib-1.0.jar")]
        [InlineData("com/example/my-lib/.1/my-lib-.1.jar")]
        [InlineData("maven-metadata.xml")]
        [InlineData("my-lib/maven-metadata.xml")]
        public void RejectsPathsOutsideTheLayout(string text)
        {
            Assert.False(MavenPath.TryParse(text, out _));
        }

        [Fact]
        public void RejectsNamesLongerThanTheDatabaseAllows()
        {
            var artifactId = new string('a', 120);

            Assert.False(MavenPath.TryParse($"com/example/{artifactId}/1.0/{artifactId}-1.0.jar", out _));
        }
    }
}
