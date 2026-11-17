using System.Linq;
using PaGetto.Core.CocoaPods;
using Xunit;

namespace PaGetto.Core.Tests.CocoaPods;

public class CocoaPodsVersionTests
{
    public class TryParse
    {
        [Theory]
        [InlineData("1", false)]
        [InlineData("1.0.0", false)]
        [InlineData("10.21.3.4", false)]
        [InlineData("1.0.0-beta.1", true)]
        [InlineData("1.0.0-2", true)]
        [InlineData("1.0.b1", true)]
        [InlineData("1.0.0+build.7", false)]
        [InlineData("1.0.0-rc.1+exp.sha.5114f85", true)]
        public void AcceptsCocoaPodsVersions(string text, bool isPrerelease)
        {
            Assert.True(CocoaPodsVersion.TryParse(text, out var version));
            Assert.Equal(text, version.Text);
            Assert.Equal(isPrerelease, version.IsPrerelease);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("v1.0.0")]
        [InlineData(".1")]
        [InlineData("1..0")]
        [InlineData("1.0.")]
        [InlineData("1.0 beta")]
        [InlineData("1.0/2")]
        [InlineData("1.0+")]
        public void RejectsEverythingElse(string text)
        {
            Assert.False(CocoaPodsVersion.TryParse(text, out _));
        }
    }

    public class CompareTo
    {
        // From the examples of CocoaPods' version_spec.rb and semver's precedence.
        [Theory]
        [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
        [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
        [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
        [InlineData("1.0.0-beta", "1.0.0-beta.2")]
        [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
        [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
        [InlineData("1.0.0-rc.1", "1.0.0")]
        [InlineData("1.0.0", "1.0.1")]
        [InlineData("1.9.0", "1.10.0")]
        [InlineData("1.0.0", "1.0.0.1")]
        [InlineData("0.9.9", "1.0.0-beta")]
        [InlineData("1.0", "1.0.0")]
        [InlineData("1.0.0+1", "1.0.0+2")]
        public void OrdersVersions(string lower, string higher)
        {
            Assert.True(Compare(lower, higher) < 0);
            Assert.True(Compare(higher, lower) > 0);
        }

        [Fact]
        public void SortsTextsThatAreNotVersionsFirst()
        {
            var sorted = new[] { "2.0.0", "not a version", "1.0.0" }.Order(CocoaPodsVersion.StringComparer);

            Assert.Equal(new[] { "not a version", "1.0.0", "2.0.0" }, sorted);
        }

        private static int Compare(string left, string right)
        {
            Assert.True(CocoaPodsVersion.TryParse(left, out var leftVersion));
            Assert.True(CocoaPodsVersion.TryParse(right, out var rightVersion));

            return leftVersion.CompareTo(rightVersion);
        }
    }
}
