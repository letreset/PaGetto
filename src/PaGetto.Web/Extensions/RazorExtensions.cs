using System;
using System.Globalization;
using System.Linq;
using Humanizer;

namespace PaGetto.Web.Extensions;

public static class RazorExtensions
{
    public static string ToMetric(this long value)
    {
        // Humanizer formats with the current culture and has no culture parameter, so switch to the
        // invariant culture to render the same text (e.g. "1.2k") regardless of the server culture.
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            return ((double) value).ToMetric();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    /// <summary>
    /// Formats a size with binary units and the invariant culture, e.g. "812 B", "2.43 MB".
    /// </summary>
    public static string ToFileSize(this long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];

        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return size.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    /// <summary>
    /// Formats a size limit given in MiB with the same units as <see cref="ToFileSize"/>, e.g. "500 MB", "8 GB".
    /// </summary>
    public static string ToSizeLimit(this uint mebibytes)
    {
        return ((long)mebibytes * 1024 * 1024).ToFileSize();
    }

    /// <summary>
    /// The two letters shown on a package tile: the start of the last dot segment, e.g. "CO" for "Contoso.Core".
    /// </summary>
    public static string ToPackageInitials(this string packageId)
    {
        if (string.IsNullOrEmpty(packageId)) return "?";

        var lastSegment = packageId[(packageId.LastIndexOf('.') + 1)..];
        if (lastSegment.Length == 0) lastSegment = packageId;

        return lastSegment[..Math.Min(2, lastSegment.Length)].ToUpperInvariant();
    }

    /// <summary>
    /// The avatar letters of a person: the first letter of up to two words, e.g. "AM" for "Alice Martin".
    /// </summary>
    public static string ToNameInitials(this string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";

        var words = name.Split([' ', '-', '_', '.', '@'], StringSplitOptions.RemoveEmptyEntries);
        var letters = string.Concat(words.Take(2).Select(w => w[0]));

        return letters.ToUpperInvariant();
    }

    /// <summary>
    /// A stable hue (0-359) for tinting a package tile, so a package keeps its color on every page.
    /// </summary>
    public static int ToHue(this string value)
    {
        var hash = 0;
        foreach (var c in (value ?? string.Empty).ToLowerInvariant())
        {
            hash = unchecked((hash * 31) + c);
        }

        return (int)((uint)hash % 360);
    }
}
