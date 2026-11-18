using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PaGetto.Core.Vsix;

/// <summary>
/// The version of a Visual Studio extension, as <see cref="System.Version"/> reads it: two to four
/// numbers separated by dots, such as <c>1.2</c> or <c>17.0.3.1</c>. Visual Studio compares versions
/// that way, so a version that has more numbers is newer than one it starts with (<c>1.0</c> is
/// older than <c>1.0.0</c>), and leading zeros don't count (<c>1.01</c> is <c>1.1</c>). A version is
/// written without leading zeros, which identifies it. Extensions have no prereleases.
/// See: https://learn.microsoft.com/visualstudio/extensibility/vsix-extension-schema-2-0-reference
/// </summary>
public sealed partial class VsixVersion : IComparable<VsixVersion>
{
    /// <summary>
    /// The longest version accepted. It is far below the database column.
    /// </summary>
    public const int MaxLength = 64;

    private readonly Version _version;

    private VsixVersion(Version version)
    {
        _version = version;
        Text = version.ToString();
    }

    /// <summary>
    /// The version without leading zeros, e.g. <c>1.2.0</c> for <c>1.02.0</c>. It identifies the version.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Compares version strings in Visual Studio's order. Strings that are not versions come first, in ordinal order.
    /// </summary>
    public static IComparer<string> StringComparer { get; } = Comparer<string>.Create(CompareStrings);

    public static bool TryParse(string text, out VsixVersion version)
    {
        version = null;
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength || !VersionRegex().IsMatch(text))
        {
            return false;
        }

        // Each number must fit an int, as Version requires.
        var numbers = text.Split('.');
        foreach (var number in numbers)
        {
            if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }
        }

        version = new VsixVersion(Version.Parse(text));
        return true;
    }

    public int CompareTo(VsixVersion other)
    {
        return other == null ? 1 : _version.CompareTo(other._version);
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

    [GeneratedRegex(@"^[0-9]+(?:\.[0-9]+){1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}
