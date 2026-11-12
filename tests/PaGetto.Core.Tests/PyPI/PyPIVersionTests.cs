using System;
using System.Linq;
using PaGetto.Core.PyPI;
using Xunit;

namespace PaGetto.Core.Tests.PyPI;

public class PyPIVersionTests
{
    public class TryParse
    {
        [Theory]
        // The spellings that PEP 440 normalizes.
        [InlineData("1.0", "1.0")]
        [InlineData("v1.0", "1.0")]
        [InlineData(" 1.0 ", "1.0")]
        [InlineData("1.01", "1.1")]
        [InlineData("1.0a1", "1.0a1")]
        [InlineData("1.0-alpha.1", "1.0a1")]
        [InlineData("1.0.BETA2", "1.0b2")]
        [InlineData("1.0c1", "1.0rc1")]
        [InlineData("1.0-pre1", "1.0rc1")]
        [InlineData("1.0preview", "1.0rc0")]
        [InlineData("1.0rc", "1.0rc0")]
        [InlineData("1.0-1", "1.0.post1")]
        [InlineData("1.0.rev2", "1.0.post2")]
        [InlineData("1.0-r", "1.0.post0")]
        [InlineData("1.0.dev", "1.0.dev0")]
        [InlineData("1.0-dev_3", "1.0.dev3")]
        [InlineData("1!2.0", "1!2.0")]
        [InlineData("0!2.0", "2.0")]
        [InlineData("1.0+Ubuntu-1", "1.0+ubuntu.1")]
        [InlineData("1.0rc1.post2.dev3+local_7", "1.0rc1.post2.dev3+local.7")]
        public void NormalizesTheVersion(string version, string expected)
        {
            Assert.True(PyPIVersion.TryParse(version, out var result));
            Assert.Equal(expected, result.Value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1.0-SNAPSHOT")]
        [InlineData("latest")]
        [InlineData("1.0.0-beta.1+")]
        [InlineData("1..0")]
        [InlineData("0.1dev-r1234")]
        public void RejectsWhatIsNotAVersion(string version)
        {
            Assert.False(PyPIVersion.TryParse(version, out _));
            Assert.Null(PyPIVersion.NormalizeOrNull(version));
        }

        [Fact]
        public void RejectsAVersionTooLongToStore()
        {
            Assert.False(PyPIVersion.TryParse("1." + string.Join('.', Enumerable.Repeat("0", 70)), out _));
        }

        [Theory]
        [InlineData("1.0a1", true)]
        [InlineData("1.0rc1", true)]
        [InlineData("1.0.dev1", true)]
        [InlineData("1.0.post1.dev1", true)]
        [InlineData("1.0", false)]
        [InlineData("1.0.post1", false)]
        [InlineData("1.0+local", false)]
        public void KnowsPrereleases(string version, bool isPrerelease)
        {
            Assert.Equal(isPrerelease, PyPIVersion.Parse(version).IsPrerelease);
        }
    }

    public class CompareTo
    {
        [Fact]
        public void OrdersVersionsAsPep440Does()
        {
            // The example of PEP 440's "Summary of permitted suffixes and relative ordering", lowest first.
            AssertOrdered(
            [
                "1.dev0", "1.0.dev456", "1.0a1", "1.0a2.dev456", "1.0a12.dev456", "1.0a12", "1.0b1.dev456", "1.0b2",
                "1.0b2.post345.dev456", "1.0b2.post345", "1.0rc1.dev456", "1.0rc1", "1.0", "1.0+abc.5", "1.0+abc.7",
                "1.0+5", "1.0.post456.dev34", "1.0.post456", "1.0.15", "1.1.dev1",
            ]);
        }

        [Fact]
        public void OrdersNumbersNumericallyAndEpochsFirst()
        {
            AssertOrdered(["1.9", "1.10", "2.0", "2024.1", "99999999999999999999.0", "1!0.1"]);
        }

        [Theory]
        [InlineData("1.0", "1.0.0")]
        [InlineData("1.0", "v1.0")]
        [InlineData("1.0a1", "1.0-ALPHA1")]
        [InlineData("1.0.post0", "1.0-0")]
        [InlineData("1.0+a.1", "1.0+A-1")]
        public void TreatsEquivalentVersionsAsEqual(string left, string right)
        {
            Assert.Equal(0, PyPIVersion.Parse(left).CompareTo(PyPIVersion.Parse(right)));
        }

        private static void AssertOrdered(string[] versions)
        {
            for (var i = 0; i < versions.Length - 1; i++)
            {
                var lower = PyPIVersion.Parse(versions[i]);
                var higher = PyPIVersion.Parse(versions[i + 1]);

                Assert.True(lower.CompareTo(higher) < 0, $"{versions[i]} should be lower than {versions[i + 1]}");
                Assert.True(higher.CompareTo(lower) > 0, $"{versions[i + 1]} should be higher than {versions[i]}");
            }
        }
    }

    public class StringComparer
    {
        [Fact]
        public void SortsStringsThatAreNotVersionsFirst()
        {
            var sorted = new[] { "2.0", "not a version", "1.0" }.Order(PyPIVersion.StringComparer).ToArray();

            Assert.Equal(new[] { "not a version", "1.0", "2.0" }, sorted);
        }
    }

    public class Parse
    {
        [Fact]
        public void ThrowsForWhatIsNotAVersion()
        {
            Assert.Throws<FormatException>(() => PyPIVersion.Parse("latest"));
        }
    }
}
