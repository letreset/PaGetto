using System.Linq;
using PaGetto.Core.Maven;
using Xunit;

namespace PaGetto.Core.Tests.Maven;

public class MavenVersionTests
{
    public class CompareTo
    {
        [Fact]
        public void OrdersQualifiersAsMavenDoes()
        {
            // From Maven's ComparableVersionTest, lowest first.
            var versions = new[]
            {
                "1-alpha2snapshot", "1-alpha2", "1-alpha-123", "1-beta-2", "1-beta123", "1-m2", "1-m11", "1-rc",
                "1-cr2", "1-rc123", "1-SNAPSHOT", "1", "1-sp", "1-sp2", "1-sp123", "1-abc", "1-def", "1-pom-1",
                "1-1-snapshot", "1-1", "1-2", "1-123",
            };

            AssertOrdered(versions);
        }

        [Fact]
        public void OrdersNumbersNumerically()
        {
            AssertOrdered(["2.0", "2-1", "2.0.2", "2.0.123", "2.1.0", "2.1-a", "2.1b", "2.1-c", "2.1-1", "2.1.0.1", "2.2", "2.123", "11.a2", "11.a11", "11.b2", "11.b11", "11.m2", "11.m11", "11", "11.a", "11b", "11c", "11m"]);
        }

        [Theory]
        [InlineData("1", "1.0")]
        [InlineData("1", "1.0.0")]
        [InlineData("1.0", "1.0.0-ga")]
        [InlineData("1.0", "1.0.FINAL")]
        [InlineData("1.0", "1.0-release")]
        [InlineData("1-rc1", "1-cr1")]
        [InlineData("1a1", "1-alpha-1")]
        [InlineData("1.0-SNAPSHOT", "1.0-snapshot")]
        [InlineData("00012", "12")]
        public void TreatsEquivalentVersionsAsEqual(string left, string right)
        {
            Assert.Equal(0, MavenVersion.Parse(left).CompareTo(MavenVersion.Parse(right)));
            Assert.Equal(0, MavenVersion.Parse(right).CompareTo(MavenVersion.Parse(left)));
        }

        [Fact]
        public void HandlesNumbersBeyondLong()
        {
            AssertOrdered(["9223372036854775807", "9223372036854775808", "99999999999999999999999"]);
        }

        private static void AssertOrdered(string[] versions)
        {
            for (var i = 0; i < versions.Length; i++)
            {
                for (var j = 0; j < versions.Length; j++)
                {
                    var expected = i.CompareTo(j);
                    var actual = MavenVersion.Parse(versions[i]).CompareTo(MavenVersion.Parse(versions[j]));

                    Assert.True(expected == System.Math.Sign(actual), $"{versions[i]} vs {versions[j]}: expected {expected}, got {actual}");
                }
            }

            Assert.Equal(versions, versions.Reverse().OrderBy(v => v, MavenVersion.StringComparer));
        }
    }

    public class IsPrerelease
    {
        [Theory]
        [InlineData("1.0-alpha-1")]
        [InlineData("1.0-beta")]
        [InlineData("2.0.0-M1")]
        [InlineData("2.0.0-RC2")]
        [InlineData("1.0-SNAPSHOT")]
        public void IsTrueForQualifiersBeforeTheRelease(string version)
        {
            Assert.True(MavenVersion.Parse(version).IsPrerelease);
        }

        [Theory]
        [InlineData("1.0")]
        [InlineData("5.3.0.Final")]
        [InlineData("33.0.0-jre")]
        [InlineData("1.0-sp1")]
        [InlineData("2.17.1")]
        public void IsFalseForReleases(string version)
        {
            Assert.False(MavenVersion.Parse(version).IsPrerelease);
        }
    }

    public class IsSnapshot
    {
        [Theory]
        [InlineData("1.0-SNAPSHOT", true)]
        [InlineData("SNAPSHOT", true)]
        [InlineData("1.0", false)]
        [InlineData("1.0-snapshot", false)]
        public void IsTrueForVersionsEndingWithSnapshot(string version, bool expected)
        {
            Assert.Equal(expected, MavenVersion.IsSnapshot(version));
        }
    }
}
