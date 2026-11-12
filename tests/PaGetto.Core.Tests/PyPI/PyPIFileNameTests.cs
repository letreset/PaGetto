using PaGetto.Core.PyPI;
using Xunit;

namespace PaGetto.Core.Tests.PyPI;

public class PyPIFileNameTests
{
    public class TryParse
    {
        [Theory]
        [InlineData("my_package-1.0-py3-none-any.whl", "my-package", "1.0", true, ".whl")]
        [InlineData("My_Package-2.0rc1-1-cp312-cp312-manylinux_2_17_x86_64.whl", "my.package", "2.0rc1", true, ".whl")]
        [InlineData("my_package-1.0.tar.gz", "my-package", "1.0", false, ".tar.gz")]
        [InlineData("my-package-1.0.post1.zip", "My_Package", "1.0.post1", false, ".zip")]
        // Older sdists keep the dashes of their name, and versions may have dashes too.
        [InlineData("python-dateutil-2.8.2.tar.gz", "python-dateutil", "2.8.2", false, ".tar.gz")]
        [InlineData("foo-bar-1.0-1.tar.gz", "foo-bar", "1.0.post1", false, ".tar.gz")]
        [InlineData("MY_PACKAGE-1.0.TAR.GZ", "my-package", "1.0", false, ".tar.gz")]
        public void ReadsTheVersion(string fileName, string project, string version, bool isWheel, string extension)
        {
            Assert.True(PyPIFileName.TryParse(fileName, project, out var result));
            Assert.Equal(fileName, result.FileName);
            Assert.Equal(version, result.Version);
            Assert.Equal(isWheel, result.IsWheel);
            Assert.Equal(extension, result.Extension);
            Assert.Equal(isWheel ? "bdist_wheel" : "sdist", result.PackageType);
        }

        [Theory]
        [InlineData("other-1.0-py3-none-any.whl", "my-package")]
        [InlineData("other-1.0.tar.gz", "my-package")]
        [InlineData("my_package-latest.tar.gz", "my-package")]
        [InlineData("my_package-1.0-py3-none.whl", "my-package")]
        [InlineData("my_package-1.0-py2.7.egg", "my-package")]
        [InlineData("my_package-1.0.win32.exe", "my-package")]
        [InlineData("../my_package-1.0.tar.gz", "my-package")]
        [InlineData("dir/my_package-1.0.tar.gz", "my-package")]
        [InlineData("my_package-1.0.tar.gz", "")]
        [InlineData("", "my-package")]
        [InlineData(null, "my-package")]
        public void RejectsWhatIsNotAFileOfTheProject(string fileName, string project)
        {
            Assert.False(PyPIFileName.TryParse(fileName, project, out _));
        }

        [Fact]
        public void RejectsANameTooLongToStoreWithItsMetadata()
        {
            var fileName = "my_package-1.0-py3-none-" + new string('x', 230) + ".whl";

            Assert.False(PyPIFileName.TryParse(fileName, "my-package", out _));
        }
    }
}
