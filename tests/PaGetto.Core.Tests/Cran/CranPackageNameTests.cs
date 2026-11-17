using PaGetto.Core.Cran;
using Xunit;

namespace PaGetto.Core.Tests.Cran;

public class CranPackageNameTests
{
    public class IsValid
    {
        [Theory]
        [InlineData("ggplot2")]
        [InlineData("R.utils")]
        [InlineData("Rcpp")]
        [InlineData("data.table")]
        [InlineData("aa")]
        public void AcceptsRPackageNames(string name)
        {
            Assert.True(CranPackageName.IsValid(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("a")]
        [InlineData("2fast")]
        [InlineData(".hidden")]
        [InlineData("trailing.")]
        [InlineData("under_score")]
        [InlineData("da-sh")]
        [InlineData("café")]
        [InlineData("../etc")]
        public void RejectsOtherNames(string name)
        {
            Assert.False(CranPackageName.IsValid(name));
        }

        [Fact]
        public void RejectsNamesLongerThanTheColumn()
        {
            Assert.False(CranPackageName.IsValid("a" + new string('b', CranPackageName.MaxLength)));
        }
    }

    public class TryParseFileName
    {
        [Theory]
        [InlineData("ggplot2_3.5.1.tar.gz", "ggplot2", "3.5.1")]
        [InlineData("R.utils_2.12-3.tar.gz", "R.utils", "2.12-3")]
        public void ReadsTheNameAndVersion(string fileName, string name, string version)
        {
            Assert.True(CranFileName.TryParse(fileName, out var parsedName, out var parsedVersion));
            Assert.Equal(name, parsedName);
            Assert.Equal(version, parsedVersion.Text);
        }

        [Theory]
        [InlineData("ggplot2_3.5.1.zip")]
        [InlineData("ggplot2-3.5.1.tar.gz")]
        [InlineData("ggplot2_3.5.1_x.tar.gz")]
        [InlineData("ggplot2_latest.tar.gz")]
        [InlineData("PACKAGES")]
        public void RejectsOtherFileNames(string fileName)
        {
            Assert.False(CranFileName.TryParse(fileName, out _, out _));
        }
    }
}
