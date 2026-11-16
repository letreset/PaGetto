using PaGetto.Core.Hex;
using Xunit;

namespace PaGetto.Core.Tests.Hex;

public class HexPackageNameTests
{
    public class IsValid
    {
        [Theory]
        [InlineData("a")]
        [InlineData("jason")]
        [InlineData("phoenix_live_view")]
        [InlineData("x509")]
        public void AcceptsHexNames(string name)
        {
            Assert.True(HexPackageName.IsValid(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Jason")]
        [InlineData("1abc")]
        [InlineData("_abc")]
        [InlineData("my-package")]
        [InlineData("a.b")]
        [InlineData("a/b")]
        [InlineData("ü")]
        public void RejectsEverythingElse(string name)
        {
            Assert.False(HexPackageName.IsValid(name));
        }

        [Fact]
        public void RejectsNamesLongerThanTheColumn()
        {
            Assert.True(HexPackageName.IsValid(new string('a', HexPackageName.MaxLength)));
            Assert.False(HexPackageName.IsValid(new string('a', HexPackageName.MaxLength + 1)));
        }
    }
}
