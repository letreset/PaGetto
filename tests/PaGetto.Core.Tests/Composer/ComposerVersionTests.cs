using System.Linq;
using PaGetto.Core.Composer;
using Xunit;

namespace PaGetto.Core.Tests.Composer;

public class ComposerVersionTests
{
    // The cases of Composer's own VersionParserTest.
    public class TryParse
    {
        [Theory]
        [InlineData("1.0.0", "1.0.0.0")]
        [InlineData("1.2.3.4", "1.2.3.4")]
        [InlineData("1.0.0RC1dev", "1.0.0.0-RC1-dev")]
        [InlineData("1.0.0-rC15-dev", "1.0.0.0-RC15-dev")]
        [InlineData("1.0.0.RC.15-dev", "1.0.0.0-RC15-dev")]
        [InlineData("1.0.0-rc1", "1.0.0.0-RC1")]
        [InlineData("1.0.0.pl3-dev", "1.0.0.0-patch3-dev")]
        [InlineData("1.0-dev", "1.0.0.0-dev")]
        [InlineData("0", "0.0.0.0")]
        [InlineData("10.4.13-beta", "10.4.13.0-beta")]
        [InlineData("10.4.13beta2", "10.4.13.0-beta2")]
        [InlineData("10.4.13beta.2", "10.4.13.0-beta2")]
        [InlineData("v1.13.11-beta.0", "1.13.11.0-beta0")]
        [InlineData("10.4.13-b5", "10.4.13.0-beta5")]
        [InlineData("v1.0.0", "1.0.0.0")]
        [InlineData("2010.01", "2010.01.0.0")]
        [InlineData("2010.1.555", "2010.1.555.0")]
        [InlineData("v20100102", "20100102")]
        [InlineData("2010-01-02", "2010.01.02")]
        [InlineData("2010-01-02.5", "2010.01.02.5")]
        [InlineData("20100102-203040", "20100102.203040")]
        [InlineData("20100102203040-10", "20100102203040.10")]
        [InlineData("20100102-203040-p1", "20100102.203040-patch1")]
        [InlineData("201903.0-p2", "201903.0-patch2")]
        [InlineData("dev-master", "dev-master")]
        [InlineData("master", "dev-master")]
        [InlineData("1.x-dev", "1.9999999.9999999.9999999-dev")]
        [InlineData("v2.0.x-dev", "2.0.9999999.9999999-dev")]
        [InlineData("dev-feature/foo", "dev-feature/foo")]
        [InlineData("DEV-FOOBAR", "dev-FOOBAR")]
        [InlineData("1.0.0-beta.5+foo", "1.0.0.0-beta5")]
        [InlineData("1.0.0+foo", "1.0.0.0")]
        [InlineData("1.0.0-alpha-2.1-3+foo", "1.0.0.0-alpha2.1-3")]
        [InlineData("00.01.03.04", "00.01.03.04")]
        [InlineData("0700", "0700.0.0.0")]
        [InlineData("041.x-dev", "041.9999999.9999999.9999999-dev")]
        [InlineData(" 1.0.0 ", "1.0.0.0")]
        public void NormalizesAsComposerDoes(string text, string normalized)
        {
            Assert.True(ComposerVersion.TryParse(text, out var version));
            Assert.Equal(normalized, version.Normalized);
            Assert.Equal(text.Trim(), version.Text);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1.0.0-meh")]
        [InlineData("1.0.0.0.0")]
        [InlineData("feature-foo")]
        [InlineData("1.0.1-SNAPSHOT")]
        [InlineData("1.0.0<1.0.5-dev")]
        [InlineData("foo bar-dev")]
        public void RejectsWhatComposerRejects(string text)
        {
            Assert.False(ComposerVersion.TryParse(text, out _));
        }

        [Theory]
        [InlineData("1", "stable")]
        [InlineData("v3.2.1", "stable")]
        [InlineData("3.1.2-p1", "stable")]
        [InlineData("3.1.2-patch", "stable")]
        [InlineData("3.0-RC2", "RC")]
        [InlineData("2.0.0rc1", "RC")]
        [InlineData("3.1.2-alpha5", "alpha")]
        [InlineData("1.2_a1", "alpha")]
        [InlineData("1.0.0-alpha11+cs-1.1.0", "alpha")]
        [InlineData("3.1.2-beta", "beta")]
        [InlineData("2.0B1", "beta")]
        [InlineData("dev-master", "dev")]
        [InlineData("3.1.2-dev", "dev")]
        [InlineData("v2.0.x-dev", "dev")]
        [InlineData("dev-feature+issue-1", "dev")]
        public void ReadsTheStability(string text, string stability)
        {
            Assert.True(ComposerVersion.TryParse(text, out var version));
            Assert.Equal(stability, version.Stability);
            Assert.Equal(stability != "stable", version.IsPrerelease);
            Assert.Equal(stability == "dev", version.IsDev);
        }
    }

    public class IsStorable
    {
        [Theory]
        [InlineData("1.0.0", true)]
        [InlineData("v1.2.3-beta.1+build.5", true)]
        [InlineData("dev-main", true)]
        [InlineData("1.x-dev", true)]
        [InlineData("dev-feature/foo", false)]
        [InlineData("dev-foo bar", false)]
        [InlineData(".1", false)]
        [InlineData("", false)]
        public void AcceptsVersionsThatAreSafeInUrlsAndPaths(string text, bool storable)
        {
            Assert.Equal(storable, ComposerVersion.IsStorable(text));
        }
    }

    public class StringComparer
    {
        [Fact]
        public void OrdersAsComposerDoes()
        {
            var versions = new[]
            {
                "1.x-dev", "1.10.0", "v1.0.1", "1.0.0-patch1", "1.0.0", "1.0.0-RC1", "1.0.0-beta1", "1.0.0-beta10", "1.0.0-alpha", "dev-main", "1.9.0",
            };

            var sorted = versions.OrderBy(v => v, ComposerVersion.StringComparer).ToArray();

            Assert.Equal(
                new[] { "dev-main", "1.0.0-alpha", "1.0.0-beta1", "1.0.0-beta10", "1.0.0-RC1", "1.0.0", "1.0.0-patch1", "v1.0.1", "1.9.0", "1.10.0", "1.x-dev" },
                sorted);
        }

        [Fact]
        public void VersionsWrittenDifferentlyAreEqualInNormalizedOrder()
        {
            Assert.True(ComposerVersion.TryParse("v1.0.0", out var v));
            Assert.True(ComposerVersion.TryParse("1.0", out var plain));

            Assert.Equal(0, ComposerVersion.CompareNormalized(v.Normalized, plain.Normalized));
        }
    }
}
