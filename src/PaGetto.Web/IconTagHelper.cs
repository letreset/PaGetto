using System;
using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace PaGetto.Web;

/// <summary>
/// Renders <c>&lt;icon name="copy" /&gt;</c> as an inline SVG that references a symbol of
/// <c>wwwroot/images/icons.svg</c>. The icon takes the text color of its parent.
/// </summary>
[HtmlTargetElement("icon", TagStructure = TagStructure.WithoutEndTag)]
public class IconTagHelper : TagHelper
{
    private readonly IHttpContextAccessor _accessor;

    public IconTagHelper(IHttpContextAccessor accessor)
    {
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
    }

    public string Name { get; set; }

    public int Size { get; set; } = 16;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        // The path base includes /feeds/{slug} on feed pages; FeedStaticFilePathMiddleware serves static files there too.
        var pathBase = _accessor.HttpContext?.Request.PathBase.Value ?? string.Empty;
        var size = Size.ToString(CultureInfo.InvariantCulture);

        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("width", size);
        output.Attributes.SetAttribute("height", size);
        output.Attributes.SetAttribute("aria-hidden", "true");
        output.Attributes.SetAttribute("focusable", "false");
        output.AddClass("bgt-i", HtmlEncoder.Default);

        output.Content.SetHtmlContent("<use href=\"");
        output.Content.Append($"{pathBase}/_content/PaGetto.Web/images/icons.svg#{Name}");
        output.Content.AppendHtml("\"></use>");
    }
}
