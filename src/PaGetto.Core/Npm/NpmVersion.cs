using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace PaGetto.Core.Npm;

/// <summary>
/// An npm package version: a semantic version 2.0, ordered by its precedence. Build metadata does
/// not take part in the precedence (the npm client drops it when publishing), so versions that only
/// differ in it are ordered by their text.
/// </summary>
public sealed partial class NpmVersion : IComparable<NpmVersion>
{
    /// <summary>
    /// The longest version accepted. It matches the database column.
    /// </summary>
    public const int MaxLength = 128;

    private readonly BigInteger _major;
    private readonly BigInteger _minor;
    private readonly BigInteger _patch;
    private readonly string[] _preRelease;
    private readonly string _text;

    private NpmVersion(BigInteger major, BigInteger minor, BigInteger patch, string[] preRelease, string text)
    {
        _major = major;
        _minor = minor;
        _patch = patch;
        _preRelease = preRelease;
        _text = text;
    }

    public bool IsPrerelease => _preRelease.Length > 0;

    /// <summary>
    /// Compares version strings by precedence. Strings that are not versions come first, in ordinal order.
    /// </summary>
    public static IComparer<string> StringComparer { get; } = Comparer<string>.Create(CompareStrings);

    public static bool TryParse(string text, out NpmVersion version)
    {
        version = null;

        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            return false;
        }

        var match = VersionRegex().Match(text);
        if (!match.Success)
        {
            return false;
        }

        version = new NpmVersion(
            BigInteger.Parse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture),
            BigInteger.Parse(match.Groups[2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture),
            BigInteger.Parse(match.Groups[3].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture),
            match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [],
            text);

        return true;
    }

    public int CompareTo(NpmVersion other)
    {
        if (other is null) return 1;

        var result = _major.CompareTo(other._major);
        if (result != 0) return result;

        result = _minor.CompareTo(other._minor);
        if (result != 0) return result;

        result = _patch.CompareTo(other._patch);
        if (result != 0) return result;

        // A prerelease comes before its release.
        if (IsPrerelease != other.IsPrerelease) return IsPrerelease ? -1 : 1;

        result = ComparePreRelease(_preRelease, other._preRelease);
        if (result != 0) return result;

        // Equal precedence: only the build metadata differs.
        return string.CompareOrdinal(_text, other._text);
    }

    public override string ToString()
    {
        return _text;
    }

    private static int CompareStrings(string x, string y)
    {
        var xIsVersion = TryParse(x, out var xVersion);
        var yIsVersion = TryParse(y, out var yVersion);

        if (xIsVersion && yIsVersion) return xVersion.CompareTo(yVersion);
        if (xIsVersion != yIsVersion) return xIsVersion ? 1 : -1;

        return string.CompareOrdinal(x, y);
    }

    private static int ComparePreRelease(string[] x, string[] y)
    {
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            // The shorter list comes first when it is a prefix of the longer one.
            if (i >= x.Length) return -1;
            if (i >= y.Length) return 1;

            var xIsNumber = BigInteger.TryParse(x[i], NumberStyles.None, CultureInfo.InvariantCulture, out var xNumber);
            var yIsNumber = BigInteger.TryParse(y[i], NumberStyles.None, CultureInfo.InvariantCulture, out var yNumber);

            int result;
            if (xIsNumber && yIsNumber)
            {
                result = xNumber.CompareTo(yNumber);
            }
            else if (xIsNumber != yIsNumber)
            {
                // Numbers come before text.
                result = xIsNumber ? -1 : 1;
            }
            else
            {
                result = string.CompareOrdinal(x[i], y[i]);
            }

            if (result != 0) return result;
        }

        return 0;
    }

    // Semantic versioning 2.0: no leading zeros in numbers, including numeric prerelease identifiers.
    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}
