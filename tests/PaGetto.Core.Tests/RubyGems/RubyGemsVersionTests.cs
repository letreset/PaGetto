using System.Linq;
using PaGetto.Core.RubyGems;
using Xunit;

namespace PaGetto.Core.Tests.RubyGems;

public class RubyGemsVersionTests
{
    public class TryParse
    {
        [Theory]
        [InlineData("1", false)]
        [InlineData("1.2.3", false)]
        [InlineData("13.0.6", false)]
        [InlineData("2.0.0.rc1", true)]
        [InlineData("1.0.0.pre.beta.2", true)]
        [InlineData("1.0a", true)]
        [InlineData("7.1.3.4", false)]
        public void AcceptsGemspecVersions(string text, bool isPrerelease)
        {
            Assert.True(RubyGemsVersion.TryParse(text, out var version));
            Assert.Equal(text, version.ToString());
            Assert.Equal(isPrerelease, version.IsPrerelease);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("a")]
        [InlineData("1.")]
        [InlineData("1..2")]
        [InlineData(".1")]
        [InlineData("1.0-beta")]
        [InlineData("1.0 ")]
        [InlineData("1.0/../x")]
        [InlineData("1.0\n")]
        public void RejectsEverythingElse(string text)
        {
            Assert.False(RubyGemsVersion.TryParse(text, out _));
        }
    }

    public class CompareTo
    {
        [Theory]
        [InlineData("1.0", "1")]
        [InlineData("1.0.0", "1")]
        [InlineData("1.01", "1.1")]
        [InlineData("1.0.a", "1.a")]
        [InlineData("1.0.a.0", "1.a")]
        public void IgnoresZerosThatDoNotChangeTheVersion(string left, string right)
        {
            Assert.Equal(0, RubyGemsVersion.Parse(left).CompareTo(RubyGemsVersion.Parse(right)));
        }

        [Theory]
        [InlineData("1.9", "1.10")]
        [InlineData("1.0.a", "1.0")]
        [InlineData("1.0.rc1", "1.0")]
        [InlineData("1.0.beta", "1.0.rc")]
        [InlineData("1.0.rc1", "1.0.rc2")]
        [InlineData("1.0.a", "1.0.0.1")]
        [InlineData("1.0.B", "1.0.a")]
        [InlineData("99999999999999999999", "100000000000000000000")]
        public void OrdersAsGemVersion(string lower, string higher)
        {
            Assert.True(RubyGemsVersion.Parse(lower).CompareTo(RubyGemsVersion.Parse(higher)) < 0);
            Assert.True(RubyGemsVersion.Parse(higher).CompareTo(RubyGemsVersion.Parse(lower)) > 0);
        }
    }

    public class StringComparer
    {
        [Fact]
        public void SortsVersionsLowestFirst()
        {
            var sorted = new[] { "1.10.0", "1.0.0.rc1", "1.9.0", "1.0.0", "2.0.0.pre", "invalid" }.Order(RubyGemsVersion.StringComparer);

            Assert.Equal(new[] { "invalid", "1.0.0.rc1", "1.0.0", "1.9.0", "1.10.0", "2.0.0.pre" }, sorted);
        }
    }
}
