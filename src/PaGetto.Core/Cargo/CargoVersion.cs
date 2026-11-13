using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace PaGetto.Core.Cargo;

/// <summary>
/// A crate version: semantic versioning 2.0, ordered the way Cargo orders versions. The build
/// metadata only breaks ties, and like crates.io, a feed has one version per version without its
/// build metadata (see <see cref="Precedence"/>).
/// </summary>
public sealed partial class CargoVersion : IComparable<CargoVersion>
{
    /// <summary>
    /// The longest version accepted. It matches the database column.
    /// </summary>
    public const int MaxLength = 128;

    private readonly BigInteger _major;
    private readonly BigInteger _minor;
    private readonly BigInteger _patch;
    private readonly string[] _preRelease;
    private readonly string[] _build;
    private readonly string _text;

    private CargoVersion(BigInteger major, BigInteger minor, BigInteger patch, string[] preRelease, string[] build, string text)
    {
        _major = major;
        _minor = minor;
        _patch = patch;
        _preRelease = preRelease;
        _build = build;
        _text = text;
    }

    public bool IsPrerelease => _preRelease.Length > 0;

    /// <summary>
    /// The version without its build metadata, e.g. <c>1.0.0</c> for <c>1.0.0+abc</c>.
    /// </summary>
    public string Precedence
    {
        get
        {
            var plus = _text.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? _text : _text[..plus];
        }
    }

    /// <summary>
    /// Compares version strings in Cargo's order. Strings that are not versions come first, in ordinal order.
    /// </summary>
    public static IComparer<string> StringComparer { get; } = Comparer<string>.Create(CompareStrings);

    public static bool TryParse(string text, out CargoVersion version)
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

        var preRelease = SplitIdentifiers(match.Groups[4]);

        // Numeric prerelease identifiers have no leading zeros either.
        foreach (var identifier in preRelease)
        {
            if (identifier.Length > 1 && identifier[0] == '0' && IsNumber(identifier))
            {
                return false;
            }
        }

        version = new CargoVersion(
            BigInteger.Parse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture),
            BigInteger.Parse(match.Groups[2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture),
            BigInteger.Parse(match.Groups[3].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture),
            preRelease,
            SplitIdentifiers(match.Groups[5]),
            text);

        return true;
    }

    public int CompareTo(CargoVersion other)
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

        result = CompareIdentifiers(_preRelease, other._preRelease);
        if (result != 0) return result;

        // A build comes after the version without one.
        return CompareIdentifiers(_build, other._build);
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

    private static string[] SplitIdentifiers(Group group)
    {
        return group.Success ? group.Value.Split('.') : [];
    }

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}
