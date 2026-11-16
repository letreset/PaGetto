using System.Linq;
using PaGetto.Core.Hex;
using Xunit;

namespace PaGetto.Core.Tests.Hex;

public class HexVersionTests
{
    public class TryParse
    {
        [Theory]
        [InlineData("1.0.0", false)]
        [InlineData("0.1.0-rc.1", true)]
        [InlineData("2.0.0-dev+abc", true)]
        [InlineData("1.2.3+build.1", false)]
        public void ReadsSemanticVersions(string text, bool isPrerelease)
        {
            Assert.True(HexVersion.TryParse(text, out var version));
            Assert.Equal(text, version.ToString());
            Assert.Equal(isPrerelease, version.IsPrerelease);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1.0")]
        [InlineData("v1.0.0")]
        [InlineData("1.02.0")]
        [InlineData("1.0.0-")]
        [InlineData("1.0.0/x")]
        public void RejectsOtherText(string text)
        {
            Assert.False(HexVersion.TryParse(text, out _));
        }

        [Fact]
        public void PrecedenceLeavesOutBuildMetadata()
        {
            Assert.True(HexVersion.TryParse("1.0.0-rc.1+abc", out var version));
            Assert.Equal("1.0.0-rc.1", version.Precedence);
        }
    }

    public class StringComparer
    {
        [Fact]
        public void OrdersVersionsAsElixirDoes()
        {
            var sorted = new[] { "1.10.0", "1.0.0", "1.0.0-rc.10", "1.0.0-rc.2", "1.0.0-beta", "1.9.0" }
                .OrderBy(v => v, HexVersion.StringComparer);

            Assert.Equal(new[] { "1.0.0-beta", "1.0.0-rc.2", "1.0.0-rc.10", "1.0.0", "1.9.0", "1.10.0" }, sorted);
        }
    }
}
