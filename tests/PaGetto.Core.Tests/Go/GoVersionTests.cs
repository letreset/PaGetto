using System.Linq;
using PaGetto.Core.Go;
using Xunit;

namespace PaGetto.Core.Tests.Go;

public class GoVersionTests
{
    public class TryParse
    {
        [Theory]
        [InlineData("v1.2.3", false, false, false)]
        [InlineData("v0.0.1", false, false, false)]
        [InlineData("v1.0.0-rc.1", true, false, false)]
        [InlineData("v1.0.0-RC1", true, false, false)]
        [InlineData("v2.0.0+incompatible", false, false, true)]
        [InlineData("v0.0.0-20191109021931-daa7c04131f5", true, true, false)]
        [InlineData("v1.2.4-0.20191109021931-daa7c04131f5", true, true, false)]
        [InlineData("v1.2.3-pre.0.20191109021931-daa7c04131f5", true, true, false)]
        [InlineData("v3.0.0-20191109021931-daa7c04131f5+incompatible", true, true, true)]
        public void AcceptsCanonicalVersions(string text, bool isPrerelease, bool isPseudo, bool isIncompatible)
        {
            Assert.True(GoVersion.TryParse(text, out var version));
            Assert.Equal(text, version.ToString());
            Assert.Equal(isPrerelease, version.IsPrerelease);
            Assert.Equal(isPseudo, version.IsPseudo);
            Assert.Equal(isIncompatible, version.IsIncompatible);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1.2.3")]
        [InlineData("v1")]
        [InlineData("v1.2")]
        [InlineData("V1.2.3")]
        [InlineData("v01.2.3")]
        [InlineData("v1.2.3-01")]
        [InlineData("v1.2.3+build")]
        [InlineData("v1.2.3-")]
        [InlineData("master")]
        [InlineData("v1.2.3/../x")]
        public void RejectsEverythingElse(string text)
        {
            Assert.False(GoVersion.TryParse(text, out _));
        }
    }

    public class StringComparer
    {
        [Fact]
        public void OrdersBySemanticVersionPrecedence()
        {
            var versions = new[]
            {
                "v1.10.0", "v1.2.0", "v1.2.0-rc.1", "v1.2.0-beta.2", "v1.2.0-beta.10",
                "v0.0.0-20191109021931-daa7c04131f5", "v2.0.0+incompatible", "v1.2.0-alpha",
            };

            var sorted = versions.OrderBy(v => v, GoVersion.StringComparer).ToArray();

            Assert.Equal(
                new[]
                {
                    "v0.0.0-20191109021931-daa7c04131f5", "v1.2.0-alpha", "v1.2.0-beta.2", "v1.2.0-beta.10",
                    "v1.2.0-rc.1", "v1.2.0", "v1.10.0", "v2.0.0+incompatible",
                },
                sorted);
        }
    }
}
