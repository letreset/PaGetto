using System.Linq;
using PaGetto.Core.Pub;
using Xunit;

namespace PaGetto.Core.Tests.Pub;

public class PubVersionTests
{
    public class TryParse
    {
        [Theory]
        [InlineData("1.2.3", false)]
        [InlineData("0.0.1", false)]
        [InlineData("1.0.0+1", false)]
        [InlineData("1.0.0-dev.1", true)]
        [InlineData("2.0.0-beta+build.5", true)]
        public void AcceptsSemanticVersions(string text, bool isPrerelease)
        {
            Assert.True(PubVersion.TryParse(text, out var version));
            Assert.Equal(text, version.ToString());
            Assert.Equal(isPrerelease, version.IsPrerelease);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1.2")]
        [InlineData("1.2.3.4")]
        [InlineData("v1.2.3")]
        [InlineData("1.2.3-")]
        [InlineData("1.2.3+")]
        [InlineData("1.2.3-beta..1")]
        [InlineData("1.2.3/../x")]
        [InlineData(" 1.2.3")]
        public void RejectsEverythingElse(string text)
        {
            Assert.False(PubVersion.TryParse(text, out _));
        }

        [Fact]
        public void RejectsVersionsLongerThanTheColumn()
        {
            var text = "1.0.0-" + new string('a', PubVersion.MaxLength);

            Assert.False(PubVersion.TryParse(text, out _));
        }
    }

    public class CompareTo
    {
        [Fact]
        public void OrdersVersionsLikePub()
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
                "1.0.0+10",
                "1.0.1",
                "1.10.0",
                "2.0.0",
            };

            var sorted = ordered
                .Reverse()
                .OrderBy(v => v, PubVersion.StringComparer)
                .ToArray();

            Assert.Equal(ordered, sorted);
        }

        [Fact]
        public void BuildMetadataMakesVersionsDifferent()
        {
            Assert.True(PubVersion.TryParse("1.0.0+1", out var first));
            Assert.True(PubVersion.TryParse("1.0.0+2", out var second));

            Assert.NotEqual(0, first.CompareTo(second));
        }

        [Fact]
        public void TextThatIsNotAVersionComesFirst()
        {
            var sorted = new[] { "1.0.0", "not-a-version" }
                .OrderBy(v => v, PubVersion.StringComparer)
                .ToArray();

            Assert.Equal(new[] { "not-a-version", "1.0.0" }, sorted);
        }
    }
}
