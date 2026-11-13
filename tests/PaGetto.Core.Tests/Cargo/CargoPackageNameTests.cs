using PaGetto.Core.Cargo;
using Xunit;

namespace PaGetto.Core.Tests.Cargo;

public class CargoPackageNameTests
{
    public class IsValid
    {
        [Theory]
        [InlineData("a")]
        [InlineData("serde")]
        [InlineData("Serde_Json")]
        [InlineData("tokio-util")]
        [InlineData("a1")]
        public void AcceptsCrateNames(string name)
        {
            Assert.True(CargoPackageName.IsValid(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1abc")]
        [InlineData("_abc")]
        [InlineData("-abc")]
        [InlineData("a.b")]
        [InlineData("a/b")]
        [InlineData("über")]
        public void RejectsEverythingElse(string name)
        {
            Assert.False(CargoPackageName.IsValid(name));
        }

        [Fact]
        public void RejectsNamesLongerThanCratesIoAllows()
        {
            Assert.True(CargoPackageName.IsValid(new string('a', CargoPackageName.MaxLength)));
            Assert.False(CargoPackageName.IsValid(new string('a', CargoPackageName.MaxLength + 1)));
        }
    }

    public class GetIndexPath
    {
        [Theory]
        [InlineData("a", "1/a")]
        [InlineData("ab", "2/ab")]
        [InlineData("Abc", "3/a/abc")]
        [InlineData("cargo", "ca/rg/cargo")]
        [InlineData("Serde", "se/rd/serde")]
        public void DependsOnTheLengthOfTheLowercasedName(string name, string path)
        {
            Assert.Equal(path, CargoPackageName.GetIndexPath(name));
        }
    }

    public class GetPrefix
    {
        [Theory]
        [InlineData("Abc", "3/A")]
        [InlineData("Cargo", "Ca/rg")]
        public void KeepsTheCaseOfTheName(string name, string prefix)
        {
            Assert.Equal(prefix, CargoPackageName.GetPrefix(name));
        }
    }
}
