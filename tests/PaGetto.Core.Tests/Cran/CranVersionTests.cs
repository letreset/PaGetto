using System.Linq;
using PaGetto.Core.Cran;
using Xunit;

namespace PaGetto.Core.Tests.Cran;

public class CranVersionTests
{
    public class TryParse
    {
        [Theory]
        [InlineData("1.0", "1.0")]
        [InlineData("1.0.2", "1.0.2")]
        [InlineData("0.9-12", "0.9.12")]
        [InlineData("1.00-02", "1.0.2")]
        [InlineData("0.0.0.9000", "0.0.0.9000")]
        [InlineData("2023.10.15", "2023.10.15")]
        public void NormalizesTheNumbers(string text, string normalized)
        {
            Assert.True(CranVersion.TryParse(text, out var version));
            Assert.Equal(text, version.Text);
            Assert.Equal(normalized, version.Normalized);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("v1.0")]
        [InlineData("1.0.")]
        [InlineData("1..0")]
        [InlineData("1.0-beta")]
        [InlineData("1.0_1")]
        [InlineData("1.0 ")]
        public void RejectsWhatRDoesNotAccept(string text)
        {
            Assert.False(CranVersion.TryParse(text, out _));
        }
    }

    public class CompareTo
    {
        [Fact]
        public void ComparesNumberByNumber()
        {
            var sorted = new[] { "1.10.0", "1.0", "1.9-2", "1.0.0", "0.99", "1.9.10", "1.9-1" }
                .OrderBy(v => v, CranVersion.StringComparer)
                .ToArray();

            Assert.Equal(new[] { "0.99", "1.0", "1.0.0", "1.9-1", "1.9-2", "1.9.10", "1.10.0" }, sorted);
        }

        [Fact]
        public void VersionsThatDifferOnlyInSeparatorsOrZerosAreEqual()
        {
            CranVersion.TryParse("1.0-2", out var dash);
            CranVersion.TryParse("1.00.2", out var dot);

            Assert.Equal(0, dash.CompareTo(dot));
        }

        [Fact]
        public void StringsThatAreNotVersionsComeFirst()
        {
            var sorted = new[] { "1.0", "latest" }.OrderBy(v => v, CranVersion.StringComparer).ToArray();

            Assert.Equal(new[] { "latest", "1.0" }, sorted);
        }
    }
}
