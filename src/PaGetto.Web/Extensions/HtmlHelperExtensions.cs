using System;
using System.Globalization;
using System.Text;
using PaGetto.Web.Helper;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace PaGetto.Web.Extensions;

public static class HtmlHelperExtensions
{
    public static IHtmlContent AddReleaseMetaTags(this IHtmlHelper htmlHelper)
    {
        var builder = new StringBuilder();

        var ver = ApplicationVersionHelper.GetVersion();
        if (ver.Present)
        {
            WriteMetaTag(builder, "branch", ver.Branch);
            WriteMetaTag(builder, "commit", ver.ShortCommit);
            WriteMetaTag(builder, "time", ver.BuildDateUtc == DateTime.MinValue ? null : ver.BuildDateUtc.ToString("O"));
        }

        return new HtmlString(builder.ToString());
    }

    public static IHtmlContent AddReleaseInformationAsComment(this IHtmlHelper htmlHelper)
    {
        var builder = new StringBuilder();

        var ver = ApplicationVersionHelper.GetVersion();
        if (ver.Present)
        {
            builder.AppendLine("<!--")
                .AppendLine($"This is PaGetto version {ver.Version}.");

            if (!string.IsNullOrEmpty(ver.ShortCommit))
            {
                var commitUri = ver.CommitUri != null ? ver.CommitUri.AbsoluteUri.Replace("git://github.com", "https://github.com") : string.Empty;
                builder.AppendLine($"Deployed from {ver.ShortCommit} Link: {commitUri}");
            }

            if (!string.IsNullOrEmpty(ver.Branch))
            {
                var branchUri = ver.BranchUri != null ? ver.BranchUri.AbsoluteUri : string.Empty;
                builder.AppendLine($"Built on {ver.Branch} Link: {branchUri}");
            }

            if (ver.BuildDateUtc != DateTime.MinValue)
            {
                builder.AppendLine($"Built on {ver.BuildDateUtc.ToString("O")}");
            }

            builder.AppendLine("-->");
        }

        return new HtmlString(builder.ToString());
    }

    /// <summary>
    /// Renders a UTC timestamp as a culture-independent <c>yyyy-MM-dd</c> date (or <paramref name="text"/>),
    /// with the full UTC timestamp in the tooltip.
    /// </summary>
    /// <param name="htmlHelper">The HTML helper.</param>
    /// <param name="utc">The timestamp. PaGetto stores all timestamps in UTC.</param>
    /// <param name="text">The visible text. Defaults to the <c>yyyy-MM-dd</c> date.</param>
    public static IHtmlContent DisplayDate(this IHtmlHelper htmlHelper, DateTime utc, string text = null)
    {
        var builder = new HtmlContentBuilder();
        builder.AppendHtml("<time datetime=\"");
        builder.Append(utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        builder.AppendHtml("\" title=\"");
        builder.Append(utc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture));
        builder.AppendHtml("\">");
        builder.Append(text ?? utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        builder.AppendHtml("</time>");
        return builder;
    }

    private static void WriteMetaTag(StringBuilder builder, string name, string val)
    {
        if (!string.IsNullOrEmpty(val))
        {
            builder.AppendLine($"<meta name=\"deployment-{name}\" content=\"{val}\" />");
        }
    }
}
