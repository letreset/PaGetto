using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace PaGetto.Core.Go;

/// <summary>
/// A canonical Go module version: <c>v</c> and a semantic version without build metadata, except
/// <c>+incompatible</c>, which marks a version of major version 2 or more of a module without a
/// <c>/vN</c> suffix. Pseudo-versions such as <c>v0.0.0-20191109021931-daa7c04131f5</c> are
/// prereleases. Versions are ordered by semantic version precedence.
/// See: https://go.dev/ref/mod#versions
/// </summary>
public sealed partial class GoVersion : IComparable<GoVersion>
{
    /// <summary>
    /// The longest version accepted. It matches the database column.
    /// </summary>
    public const int MaxLength = 128;

    private readonly string[] _preRelease;
    private readonly string _text;

    private GoVersion(BigInteger major, BigInteger minor, BigInteger patch, string[] preRelease, bool isIncompatible, string text)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _preRelease = preRelease;
        IsIncompatible = isIncompatible;
        _text = text;
    }

    public BigInteger Major { get; }
    public BigInteger Minor { get; }
    public BigInteger Patch { get; }

    public bool IsPrerelease => _preRelease.Length > 0;

    /// <summary>
    /// Whether the version ends with <c>+incompatible</c>.
    /// </summary>
    public bool IsIncompatible { get; }

    /// <summary>
    /// Whether this is a pseudo-version, which names a commit without a version tag. The
    /// <c>@v/list</c> of a module leaves pseudo-versions out.
    /// </summary>
    public bool IsPseudo => IsPrerelease && PseudoVersionRegex().IsMatch(_text);

    /// <summary>
    /// Compares version strings in Go's order. Strings that are not versions come first, in ordinal order.
    /// </summary>
    public static IComparer<string> StringComparer { get; } = Comparer<string>.Create(CompareStrings);

    /// <summary>
    /// Parses a canonical version. Shorthands such as <c>v1.2</c> and build metadata other than
    /// <c>+incompatible</c> are not canonical, so they are rejected.
    /// </summary>
    public static bool TryParse(string text, out GoVersion version)
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

        var preRelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];

        // Numeric prerelease identifiers have no leading zeros.
        foreach (var identifier in preRelease)
        {
            if (identifier.Length > 1 && identifier[0] == '0' && IsNumber(identifier))
            {
                return false;
            }
        }

        version = new GoVersion(
            BigInteger.Parse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture),
            BigInteger.Parse(match.Groups[2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture),
            BigInteger.Parse(match.Groups[3].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture),
            preRelease,
            match.Groups[5].Success,
            text);

        return true;
    }

    public int CompareTo(GoVersion other)
    {
        if (other is null) return 1;

        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;

        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        // A prerelease comes before its release.
        if (IsPrerelease != other.IsPrerelease) return IsPrerelease ? -1 : 1;

        result = CompareIdentifiers(_preRelease, other._preRelease);
        if (result != 0) return result;

        // +incompatible is build metadata, which has no precedence; this only makes the order stable.
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

    private static int CompareIdentifiers(string[] x, string[] y)
    {
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            // The shorter list comes first when it is a prefix of the longer one.
            if (i >= x.Length) return -1;
            if (i >= y.Length) return 1;

            var xIsNumber = IsNumber(x[i]);
            var yIsNumber = IsNumber(y[i]);

            int result;
            if (xIsNumber && yIsNumber)
            {
                result = BigInteger.Parse(x[i], NumberStyles.None, CultureInfo.InvariantCulture)
                    .CompareTo(BigInteger.Parse(y[i], NumberStyles.None, CultureInfo.InvariantCulture));
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

    private static bool IsNumber(string identifier)
    {
        foreach (var c in identifier)
        {
            if (c is < '0' or > '9') return false;
        }

        return true;
    }

    [GeneratedRegex(@"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(\+incompatible)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    // The pseudo-version forms vX.0.0-yyyymmddhhmmss-abcdefabcdef, vX.Y.Z-pre.0.yyyymmddhhmmss-abcdefabcdef
    // and vX.Y.(Z+1)-0.yyyymmddhhmmss-abcdefabcdef, as the go command recognizes them.
    [GeneratedRegex(@"^v[0-9]+\.(0\.0-|[0-9]+\.[0-9]+-([^+]*\.)?0\.)[0-9]{14}-[A-Za-z0-9]+(\+incompatible)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PseudoVersionRegex();
}
