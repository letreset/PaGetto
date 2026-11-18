using System.Linq;
using PaGetto.Core.Vsix;
using Xunit;

namespace PaGetto.Core.Tests.Vsix;

public class VsixVersionTests
{
    public class TryParse
    {
        [Theory]
        [InlineData("1.0", "1.0")]
        [InlineData("1.0.0", "1.0.0")]
        [InlineData("17.0.3.1", "17.0.3.1")]
        [InlineData("1.01", "1.1")]
        [InlineData("01.002.0", "1.2.0")]
        public void WritesTheVersionWithoutLeadingZeros(string text, string expected)
        {
            Assert.True(VsixVersion.TryParse(text, out var version));
            Assert.Equal(expected, version.Text);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("1.0.0.0.0")]
        [InlineData("v1.0")]
        [InlineData("1.0-beta")]
        [InlineData("1.0.0-preview.1")]
        [InlineData("1..0")]
        [InlineData("1.0 ")]
        [InlineData("1.2147483648")]
        public void RejectsWhatVisualStudioDoesNotAccept(string text)
        {
            Assert.False(VsixVersion.TryParse(text, out _));
        }
    }

    public class CompareTo
    {
        [Fact]
        public void ComparesNumberByNumberAndMoreNumbersAreNewer()
        {
            var sorted = new[] { "1.10", "1.0.0", "1.9.1", "1.0", "0.99", "1.0.0.1" }
                .OrderBy(v => v, VsixVersion.StringComparer)
                .ToArray();

            Assert.Equal(new[] { "0.99", "1.0", "1.0.0", "1.0.0.1", "1.9.1", "1.10" }, sorted);
        }

        [Fact]
        public void StringsThatAreNotVersionsComeFirst()
        {
            var sorted = new[] { "1.0", "latest" }.OrderBy(v => v, VsixVersion.StringComparer).ToArray();

            Assert.Equal(new[] { "latest", "1.0" }, sorted);
        }
    }
}
