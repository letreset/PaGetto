using System;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using PaGetto.Web.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Moq;
using Xunit;

namespace PaGetto.Web.Tests.Extensions;

public class HtmlHelperExtensionsFacts
{
    public class DisplayDate
    {
        private static readonly DateTime Timestamp = new(2026, 9, 5, 14, 3, 7, DateTimeKind.Utc);

        [Theory]
        [InlineData("en-US")]
        [InlineData("tr-TR")]
        [InlineData("th-TH")]
        [InlineData("ar-SA")]
        public void RendersSameOutputInEveryCulture(string culture)
        {
            var html = RenderInCulture(culture, h => h.DisplayDate(Timestamp));

            Assert.Equal(
                "<time datetime=\"2026-09-05T14:03:07Z\" title=\"2026-09-05 14:03:07 UTC\">2026-09-05</time>",
                html);
        }

        [Fact]
        public void UsesCustomTextAndEncodesIt()
        {
            var html = RenderInCulture("en-US", h => h.DisplayDate(Timestamp, "<3 days ago"));

            Assert.Equal(
                "<time datetime=\"2026-09-05T14:03:07Z\" title=\"2026-09-05 14:03:07 UTC\">&lt;3 days ago</time>",
                html);
        }

        private static string RenderInCulture(string culture, Func<IHtmlHelper, IHtmlContent> render)
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);

                var content = render(Mock.Of<IHtmlHelper>());
                using var writer = new StringWriter();
                content.WriteTo(writer, HtmlEncoder.Default);
                return writer.ToString();
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }
}
