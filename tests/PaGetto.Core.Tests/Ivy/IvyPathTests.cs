using PaGetto.Core.Ivy;
using PaGetto.Core.Maven;
using Xunit;

namespace PaGetto.Core.Tests.Ivy;

public class IvyPathTests
{
    public class TryParse
    {
        [Fact]
        public void ReadsAFileOfAnSbtPlugin()
        {
            Assert.True(IvyPath.TryParse("com.example/sbt-Tool/scala_2.12/sbt_1.0/1.0.0/jars/sbt-Tool.jar", out var path));

            Assert.False(path.IsDirectory);
            Assert.Equal("com.example", path.Organisation);
            Assert.Equal("sbt-Tool", path.Module);
            Assert.Equal("2.12", path.ScalaVersion);
            Assert.Equal("1.0", path.SbtVersion);
            Assert.Equal("1.0.0", path.Revision);
            Assert.Equal("jars/sbt-Tool.jar", path.FileName);
            Assert.Null(path.Checksum);
            Assert.False(path.IsDescriptor);
            Assert.Equal("com.example:sbt-tool:scala_2.12:sbt_1.0", path.PackageName);
            Assert.Equal("com.example/sbt-Tool/scala_2.12/sbt_1.0/1.0.0/jars/sbt-Tool.jar", path.FilePath);
        }

        [Fact]
        public void ReadsTheDescriptorOfALibrary()
        {
            Assert.True(IvyPath.TryParse("com.example/my-lib_2.13/1.0.0/ivys/ivy.xml", out var path));

            Assert.Null(path.ScalaVersion);
            Assert.Null(path.SbtVersion);
            Assert.Equal("ivys/ivy.xml", path.FileName);
            Assert.True(path.IsDescriptor);
            Assert.Equal("com.example:my-lib_2.13", path.PackageName);
        }

        [Theory]
        [InlineData("ivy.xml.sha1", MavenChecksum.Sha1, "ivys/ivy.xml")]
        [InlineData("ivy.xml.md5", MavenChecksum.Md5, "ivys/ivy.xml")]
        public void ReadsTheChecksumOfAFile(string fileName, MavenChecksum checksum, string checkedFileName)
        {
            Assert.True(IvyPath.TryParse($"com.example/my-lib/1.0.0/ivys/{fileName}", out var path));

            Assert.Equal(checksum, path.Checksum);
            Assert.Equal(checkedFileName, path.FileName);
            Assert.Equal($"com.example/my-lib/1.0.0/{checkedFileName}", path.FilePath);
        }

        [Theory]
        [InlineData("com.example/sbt-tool/scala_2.12/sbt_1.0/", "2.12", "1.0")]
        [InlineData("com.example/sbt-tool/scala_2.12/sbt_1.0", "2.12", "1.0")]
        [InlineData("com.example/sbt-tool/scala_2.12/", "2.12", null)]
        [InlineData("com.example/sbt-tool/", null, null)]
        public void ReadsTheFolderOfAModule(string value, string scalaVersion, string sbtVersion)
        {
            Assert.True(IvyPath.TryParse(value, out var path));

            Assert.True(path.IsDirectory);
            Assert.Equal("sbt-tool", path.Module);
            Assert.Equal(scalaVersion, path.ScalaVersion);
            Assert.Equal(sbtVersion, path.SbtVersion);
            Assert.Null(path.Revision);
            Assert.Equal(value.TrimEnd('/'), path.FilePath);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("com.example")]
        [InlineData("com.example/my-lib/1.0.0/")]
        [InlineData("com.example/my-lib/1.0.0/jars/")]
        [InlineData("com.example/my-lib/1.0.0/jars/my-lib.jar/")]
        [InlineData("com.example/my-lib/1.0.0/jar/my-lib.jar")]
        [InlineData("com.example/my-lib/1.0.0/jars/my-lib")]
        [InlineData("com.example/my-lib/branch/1.0.0/jars/my-lib.jar")]
        [InlineData("com.example/my-lib/../1.0.0/jars/my-lib.jar")]
        [InlineData("com.example/my-lib/.1/jars/my-lib.jar")]
        [InlineData("com.example/my lib/1.0.0/jars/my-lib.jar")]
        [InlineData("com.example//1.0.0/jars/my-lib.jar")]
        public void RejectsPathsOutsideTheLayout(string value)
        {
            Assert.False(IvyPath.TryParse(value, out _));
        }
    }

    public class GetFilePath
    {
        [Fact]
        public void LeavesOutTheCrossVersionsOfALibrary()
        {
            Assert.Equal("com.example/my-lib/1.0.0/jars/my-lib.jar", IvyPath.GetFilePath("com.example", "my-lib", null, null, "1.0.0", "jars/my-lib.jar"));
            Assert.Equal(
                "com.example/sbt-tool/scala_2.12/sbt_1.0/1.0.0/ivys/ivy.xml",
                IvyPath.GetFilePath("com.example", "sbt-tool", "2.12", "1.0", "1.0.0", IvyPath.DescriptorFileName));
        }
    }
}
