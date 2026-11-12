using PaGetto.Core.PyPI;
using Xunit;

namespace PaGetto.Core.Tests.PyPI;

public class PyPIPackageNameTests
{
    public class Normalize
    {
        [Theory]
        [InlineData("friendly-bard", "friendly-bard")]
        [InlineData("Friendly-Bard", "friendly-bard")]
        [InlineData("FRIENDLY-BARD", "friendly-bard")]
        [InlineData("friendly.bard", "friendly-bard")]
        [InlineData("friendly_bard", "friendly-bard")]
        [InlineData("friendly--bard", "friendly-bard")]
        [InlineData("FrIeNdLy-._.-bArD", "friendly-bard")]
        public void NormalizesAsPep503Says(string name, string expected)
        {
            Assert.Equal(expected, PyPIPackageName.Normalize(name));
        }
    }

    public class IsValid
    {
        [Theory]
        [InlineData("a")]
        [InlineData("my-package")]
        [InlineData("My_Package.2")]
        [InlineData("1password")]
        public void AcceptsValidNames(string name)
        {
            Assert.True(PyPIPackageName.IsValid(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("-my-package")]
        [InlineData("my-package.")]
        [InlineData("my package")]
        [InlineData("my/package")]
        [InlineData("pàckage")]
        public void RejectsInvalidNames(string name)
        {
            Assert.False(PyPIPackageName.IsValid(name));
        }

        [Fact]
        public void RejectsANameTooLongToStore()
        {
            Assert.True(PyPIPackageName.IsValid(new string('a', PyPIPackageName.MaxLength)));
            Assert.False(PyPIPackageName.IsValid(new string('a', PyPIPackageName.MaxLength + 1)));
        }
    }
}
