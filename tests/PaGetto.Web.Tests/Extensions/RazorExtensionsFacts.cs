using System.Globalization;
using PaGetto.Web.Extensions;
using Xunit;

namespace PaGetto.Web.Tests.Extensions;

public class RazorExtensionsFacts
{
    public class ToMetric
    {
        [Theory]
        [InlineData("en-US")]
        [InlineData("tr-TR")]
        [InlineData("de-DE")]
        public void RendersSameOutputInEveryCulture(string culture)
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUICulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                CultureInfo.CurrentUICulture = new CultureInfo(culture);

                Assert.Equal("1.234k", 1234L.ToMetric());
                Assert.Equal(culture, CultureInfo.CurrentCulture.Name);
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUICulture;
            }
        }
    }

    public class ToFileSize
    {
        [Theory]
        [InlineData(0L, "0 B")]
        [InlineData(812L, "812 B")]
        [InlineData(7787L, "7.6 KB")]
        [InlineData(2548039L, "2.43 MB")]
        [InlineData(5368709120L, "5 GB")]
        public void UsesBinaryUnits(long bytes, string expected)
        {
            Assert.Equal(expected, bytes.ToFileSize());
        }

        [Fact]
        public void UsesTheInvariantCulture()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

                Assert.Equal("7.6 KB", 7787L.ToFileSize());
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }
    }
}
