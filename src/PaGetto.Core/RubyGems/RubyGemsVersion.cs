using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// A gem version, ordered as <c>Gem::Version</c> orders them: the version is split into numbers and
/// runs of letters, trailing zeros don't count (<c>1.0</c> is <c>1</c>), and letters come before
/// numbers, so <c>1.0.a</c> is lower than <c>1.0</c>. A version with a letter is a prerelease.
/// Versions are in the form gemspecs store them, in which a <c>-</c> has become <c>.pre.</c>.
/// See: https://docs.ruby-lang.org/en/master/Gem/Version.html
/// </summary>
public sealed partial class RubyGemsVersion : IComparable<RubyGemsVersion>
{
    /// <summary>
    /// The longest version accepted, the length of the version column.
    /// </summary>
    public const int MaxLength = 128;

    private static readonly byte[] _zero = "0"u8.ToArray();

    private readonly List<object> _canonicalSegments;

    private RubyGemsVersion(string version)
    {
        Value = version;
        IsPrerelease = version.AsSpan().IndexOfAnyInRange('A', 'z') >= 0;
        _canonicalSegments = GetCanonicalSegments(version);
    }

    /// <summary>
    /// Compares version strings as gem versions. Strings that are not versions sort before versions,
    /// in ordinal order.
    /// </summary>
    public static IComparer<string> StringComparer { get; } = Comparer<string>.Create(CompareStrings);

    /// <summary>
    /// The version as it was given.
    /// </summary>
    public string Value { get; }

    public bool IsPrerelease { get; }

    public static bool TryParse(string version, out RubyGemsVersion result)
    {
        result = version != null && version.Length <= MaxLength && VersionPattern().IsMatch(version)
            ? new RubyGemsVersion(version)
            : null;

        return result != null;
    }

    public static RubyGemsVersion Parse(string version)
    {
        return TryParse(version, out var result)
            ? result
            : throw new FormatException($"'{version}' is not a gem version.");
    }

    public int CompareTo(RubyGemsVersion other)
    {
        if (other == null)
        {
            return 1;
        }

        var left = _canonicalSegments;
        var right = other._canonicalSegments;

        for (var i = 0; i < Math.Max(left.Count, right.Count); i++)
        {
            // A missing segment is a zero.
            var l = i < left.Count ? left[i] : _zero;
            var r = i < right.Count ? right[i] : _zero;

            var result = CompareSegments(l, r);
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    public override string ToString()
    {
        return Value;
    }

    private static int CompareStrings(string x, string y)
    {
        var xValid = TryParse(x, out var xVersion);
        var yValid = TryParse(y, out var yVersion);

        if (xValid && yValid)
        {
            return xVersion.CompareTo(yVersion);
        }

        if (xValid != yValid)
        {
            return xValid ? 1 : -1;
        }

        return string.CompareOrdinal(x, y);
    }

    /// <summary>
    /// A number is held as its digits without leading zeros (a byte array), so numbers of any size
    /// compare correctly; a run of letters is held as a string.
    /// </summary>
    private static int CompareSegments(object left, object right)
    {
        return (left, right) switch
        {
            (byte[] l, byte[] r) => l.Length != r.Length ? l.Length.CompareTo(r.Length) : l.AsSpan().SequenceCompareTo(r),

            // Letters come before numbers, which makes 1.0.a a prerelease of 1.0.
            (string, byte[]) => -1,
            (byte[], string) => 1,
            _ => Math.Sign(string.CompareOrdinal((string)left, (string)right)),
        };
    }

    /// <summary>
    /// The segments without the zeros that don't change the version: those at the end of the
    /// leading numbers, and those at the end of the rest. <c>1.0.0.a.0</c> is <c>1.a</c>.
    /// </summary>
    private static List<object> GetCanonicalSegments(string version)
    {
        var segments = new List<object>();
        foreach (Match match in SegmentPattern().Matches(version))
        {
            if (char.IsAsciiDigit(match.Value[0]))
            {
                var digits = match.Value.TrimStart('0');
                segments.Add(Encoding.ASCII.GetBytes(digits.Length == 0 ? "0" : digits));
            }
            else
            {
                segments.Add(match.Value);
            }
        }

        var firstLetters = segments.FindIndex(s => s is string);
        if (firstLetters < 0)
        {
            firstLetters = segments.Count;
        }

        var numbers = segments.GetRange(0, firstLetters);
        var rest = segments.GetRange(firstLetters, segments.Count - firstLetters);

        DropTrailingZeros(numbers);
        DropTrailingZeros(rest);

        numbers.AddRange(rest);
        return numbers;
    }

    private static void DropTrailingZeros(List<object> segments)
    {
        while (segments.Count > 0 && segments[^1] is byte[] { Length: 1 } digits && digits[0] == (byte)'0')
        {
            segments.RemoveAt(segments.Count - 1);
        }
    }

    [GeneratedRegex(@"\A[0-9]+(\.[0-9A-Za-z]+)*\z")]
    private static partial Regex VersionPattern();

    [GeneratedRegex("[0-9]+|[A-Za-z]+")]
    private static partial Regex SegmentPattern();
}
