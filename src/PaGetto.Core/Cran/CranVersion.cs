using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PaGetto.Core.Cran;

/// <summary>
/// A version of an R package, as R's <c>package_version</c> reads it: two or more non-negative
/// integers separated by <c>.</c> or <c>-</c>, such as <c>1.0.2</c> or <c>0.9-12</c>. Versions are
/// compared number by number, and a version that has more numbers is newer than one it starts
/// with (<c>1.0</c> is older than <c>1.0.0</c>). Two versions that differ only in their separators
/// or leading zeros (<c>1.0-2</c> and <c>1.0.2</c>) are the same version. R has no prereleases.
/// See: https://cran.r-project.org/doc/manuals/r-release/R-exts.html#The-DESCRIPTION-file
/// </summary>
public sealed partial class CranVersion : IComparable<CranVersion>
{
    /// <summary>
    /// The longest version accepted. It matches the database column.
    /// </summary>
    public const int MaxLength = 64;

    private readonly string[] _numbers;

    private CranVersion(string text, string[] numbers)
    {
        Text = text;
        _numbers = numbers;
        Normalized = string.Join('.', numbers);
    }

    /// <summary>
    /// The version as written, which names the package's file.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// The numbers of the version without leading zeros, joined with dots, e.g. <c>1.0.2</c> for
    /// <c>1.00-2</c>. It identifies the version.
    /// </summary>
    public string Normalized { get; }

    /// <summary>
    /// Compares version strings in R's order. Strings that are not versions come first, in ordinal order.
    /// </summary>
    public static IComparer<string> StringComparer { get; } = Comparer<string>.Create(CompareStrings);

    public static bool TryParse(string text, out CranVersion version)
    {
        version = null;
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength || !VersionRegex().IsMatch(text))
        {
            return false;
        }

        var numbers = text.Split(['.', '-']);
        for (var i = 0; i < numbers.Length; i++)
        {
            var trimmed = numbers[i].TrimStart('0');
            numbers[i] = trimmed.Length == 0 ? "0" : trimmed;
        }

        version = new CranVersion(text, numbers);
        return true;
    }

    public int CompareTo(CranVersion other)
    {
        if (other == null)
        {
            return 1;
        }

        for (var i = 0; i < Math.Min(_numbers.Length, other._numbers.Length); i++)
        {
            // Without leading zeros, a longer number is a larger one.
            var result = _numbers[i].Length != other._numbers[i].Length
                ? _numbers[i].Length.CompareTo(other._numbers[i].Length)
                : string.CompareOrdinal(_numbers[i], other._numbers[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return _numbers.Length.CompareTo(other._numbers.Length);
    }

    public override string ToString()
    {
        return Text;
    }

    private static int CompareStrings(string x, string y)
    {
        var xValid = TryParse(x, out var xVersion);
        var yValid = TryParse(y, out var yVersion);

        if (xValid && yValid)
        {
            var result = xVersion.CompareTo(yVersion);
            return result != 0 ? result : string.CompareOrdinal(x, y);
        }

        if (xValid != yValid)
        {
            return xValid ? 1 : -1;
        }

        return string.CompareOrdinal(x, y);
    }

    [GeneratedRegex(@"^[0-9]+(?:[.-][0-9]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}
