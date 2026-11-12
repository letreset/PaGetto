using System.Linq;
using PaGetto.Core.Npm;
using Xunit;

namespace PaGetto.Core.Tests.Npm;

public class NpmVersionTests
{
    public class TryParse
    {
        [Theory]
        [InlineData("0.0.0", false)]
        [InlineData("1.2.3", false)]
        [InlineData("1.2.3-beta.1", true)]
        [InlineData("1.2.3-0.alpha", true)]
        [InlineData("1.2.3+build.5", false)]
        [InlineData("10.20.30-rc.1+build", true)]
        public void AcceptsSemanticVersions(string text, bool isPrerelease)
        {
            Assert.True(NpmVersion.TryParse(text, out var version));
            Assert.Equal(text, version.ToString());
            Assert.Equal(isPrerelease, version.IsPrerelease);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1.2")]
        [InlineData("v1.2.3")]
        [InlineData("01.2.3")]
        [InlineData("1.2.3-01")]
        [InlineData("1.2.3-")]
        [InlineData("1.2.3 ")]
        [InlineData("1.2.3-beta_1")]
        public void RejectsAnythingElse(string text)
        {
            Assert.False(NpmVersion.TryParse(text, out _));
        }
    }

    public class StringComparer
    {
        [Fact]
        public void OrdersByPrecedence()
        {
            var versions = new[] { "1.0.0", "1.0.0-rc.1", "1.0.0-beta.11", "1.0.0-beta.2", "1.0.0-beta", "1.0.0-alpha.beta", "1.0.0-alpha.1", "1.0.0-alpha", "0.9.10", "0.9.9" };

            var sorted = versions.OrderBy(v => v, NpmVersion.StringComparer);

            Assert.Equal(versions.Reverse(), sorted);
        }

        [Fact]
        public void OrdersVersionsThatOnlyDifferInBuildMetadataByTheirText()
        {
            Assert.True(NpmVersion.StringComparer.Compare("1.0.0+a", "1.0.0+b") < 0);
            Assert.Equal(0, NpmVersion.StringComparer.Compare("1.0.0+a", "1.0.0+a"));
        }

        [Fact]
        public void PutsInvalidVersionsFirst()
        {
            Assert.True(NpmVersion.StringComparer.Compare("latest", "0.0.1") < 0);
        }
    }
}
