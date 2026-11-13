using System.Linq;
using PaGetto.Core.Cargo;
using Xunit;

namespace PaGetto.Core.Tests.Cargo;

public class CargoVersionTests
{
    public class TryParse
    {
        [Theory]
        [InlineData("1.2.3", false)]
        [InlineData("0.0.1", false)]
        [InlineData("1.0.0+abc.1", false)]
        [InlineData("1.0.0-alpha.1", true)]
        [InlineData("2.0.0-beta+build.5", true)]
        [InlineData("1.0.0-0", true)]
        public void AcceptsSemanticVersions(string text, bool isPrerelease)
        {
            Assert.True(CargoVersion.TryParse(text, out var version));
            Assert.Equal(text, version.ToString());
            Assert.Equal(isPrerelease, version.IsPrerelease);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1.2")]
        [InlineData("1.2.3.4")]
        [InlineData("v1.2.3")]
        [InlineData("01.2.3")]
        [InlineData("1.02.3")]
        [InlineData("1.2.3-01")]
        [InlineData("1.2.3-")]
        [InlineData("1.2.3+")]
        [InlineData("1.2.3/../x")]
        [InlineData(" 1.2.3")]
        public void RejectsEverythingElse(string text)
        {
            Assert.False(CargoVersion.TryParse(text, out _));
        }

        [Fact]
        public void RejectsVersionsLongerThanTheColumn()
        {
            Assert.False(CargoVersion.TryParse("1.0.0-" + new string('a', CargoVersion.MaxLength), out _));
        }
    }

    public class Precedence
    {
        [Theory]
        [InlineData("1.0.0", "1.0.0")]
        [InlineData("1.0.0+abc", "1.0.0")]
        [InlineData("1.0.0-rc.1+abc", "1.0.0-rc.1")]
        public void LeavesOutTheBuildMetadata(string text, string precedence)
        {
            Assert.True(CargoVersion.TryParse(text, out var version));
            Assert.Equal(precedence, version.Precedence);
        }
    }

    public class CompareTo
    {
        [Fact]
        public void OrdersVersionsLikeCargo()
        {
            // Each version is lower than the next one.
            var ordered = new[]
            {
                "0.9.9",
                "1.0.0-alpha",
                "1.0.0-alpha.1",
                "1.0.0-alpha.beta",
                "1.0.0-beta.2",
                "1.0.0-beta.11",
                "1.0.0-rc.1",
                "1.0.0",
                "1.0.0+1",
                "1.0.0+2",
                "1.2.0",
                "1.10.0",
                "10.0.0",
            };

            var shuffled = ordered.Reverse().ToList();
            Assert.Equal(ordered, shuffled.OrderBy(v => v, CargoVersion.StringComparer));
        }
    }
}
