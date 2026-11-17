using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PaGetto.Core.CocoaPods;

/// <summary>
/// A version of a pod, with the rules of CocoaPods' <c>Pod::Version</c>: numbers separated by dots,
/// where a part may have letters and hyphens (<c>1.0.0-beta.1</c>), and build metadata after a
/// <c>+</c>. A version with a letter or a hyphen before its build metadata is a prerelease. Versions
/// are kept as written; CocoaPods asks for a pod's files by the version text of the index.
/// See: https://github.com/CocoaPods/Core/blob/master/lib/cocoapods-core/version.rb
/// </summary>
public sealed partial class CocoaPodsVersion : IComparable<CocoaPodsVersion>
{
    /// <summary>
    /// The longest version accepted. It matches the database column.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Compares version texts with <see cref="CompareTo"/>. Texts that are not versions come first,
    /// in ordinal order.
    /// </summary>
    public static readonly IComparer<string> StringComparer = Comparer<string>.Create(CompareStrings);

    private readonly IReadOnlyList<Segment> _numericSegments;
    private readonly IReadOnlyList<Segment> _prereleaseSegments;

    private CocoaPodsVersion(string text)
    {
        Text = text;

        var withoutMetadata = MetadataRegex().Replace(text, "");
        IsPrerelease = withoutMetadata.Any(c => c == '-' || char.IsAsciiLetter(c));

        // Pod::Version#_segments: the runs of digits and of letters, without the build metadata.
        var segments = SegmentRegex().Matches(withoutMetadata)
            .Select(m => new Segment(m.Value, char.IsAsciiDigit(m.Value[0])))
            .ToList();

        // The leading numbers without trailing zeros, so 1.0 and 1.0.0 are the same release, then the rest.
        var numeric = segments.TakeWhile(s => s.IsNumber).ToList();
        while (numeric.Count > 0 && numeric[^1].IsZero)
        {
            numeric.RemoveAt(numeric.Count - 1);
        }

        _numericSegments = numeric;
        _prereleaseSegments = segments.SkipWhile(s => s.IsNumber).ToList();
    }

    /// <summary>
    /// The version as written.
    /// </summary>
    public string Text { get; }

    public bool IsPrerelease { get; }

    public static bool TryParse(string text, out CocoaPodsVersion version)
    {
        version = null;
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength || !VersionRegex().IsMatch(text))
        {
            return false;
        }

        version = new CocoaPodsVersion(text);
        return true;
    }

    /// <summary>
    /// Pod::Version#&lt;=&gt;: the release numbers, then the prerelease parts (a release comes after its
    /// prereleases), then the text, so versions that CocoaPods treats as equal (<c>1.0</c> and
    /// <c>1.0.0</c>) still have a fixed order.
    /// </summary>
    public int CompareTo(CocoaPodsVersion other)
    {
        if (other == null)
        {
            return 1;
        }

        var comparison = CompareSegments(_numericSegments, other._numericSegments, isPrerelease: false)
            ?? CompareSegments(_prereleaseSegments, other._prereleaseSegments, isPrerelease: true)
            ?? 0;

        return comparison != 0 ? comparison : Math.Sign(string.CompareOrdinal(Text, other.Text));
    }

    public override string ToString()
    {
        return Text;
    }

    private static int CompareStrings(string left, string right)
    {
        var leftParsed = TryParse(left, out var leftVersion);
        var rightParsed = TryParse(right, out var rightVersion);

        if (leftParsed && rightParsed)
        {
            return leftVersion.CompareTo(rightVersion);
        }

        if (leftParsed != rightParsed)
        {
            return leftParsed ? 1 : -1;
        }

        return string.CompareOrdinal(left, right);
    }

    /// <summary>
    /// The comparison of Pod::Version#compare_segments for one kind of segments.
    /// </summary>
    /// <returns>The order, or null when the segments are equal.</returns>
    private static int? CompareSegments(IReadOnlyList<Segment> left, IReadOnlyList<Segment> right, bool isPrerelease)
    {
        var limit = Math.Max(left.Count, right.Count);
        for (var i = 0; i < limit; i++)
        {
            var lhs = i < left.Count ? left[i] : (Segment?)null;
            var rhs = i < right.Count ? right[i] : (Segment?)null;

            if (lhs is { } l && rhs is { } r && l.EqualsSegment(r))
            {
                continue;
            }

            // A version without prerelease parts comes after one with them; otherwise a missing part
            // comes first.
            if (lhs == null)
            {
                return isPrerelease && i == 0 ? 1 : -1;
            }

            if (rhs == null)
            {
                return isPrerelease && i == 0 ? -1 : 1;
            }

            return lhs.Value.CompareSegment(rhs.Value);
        }

        return null;
    }

    // Pod::Version::VERSION_PATTERN, without the surrounding white space Pod::Version strips.
    [GeneratedRegex(@"^[0-9]+(\.[0-9a-zA-Z-]+)*(\+[0-9a-zA-Z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"\+[0-9a-zA-Z.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex MetadataRegex();

    [GeneratedRegex("[0-9]+|[a-zA-Z]+", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentRegex();

    /// <summary>
    /// A run of digits, compared by its value, or of letters, compared by its text.
    /// </summary>
    private readonly record struct Segment(string Text, bool IsNumber)
    {
        private string Number => Text.TrimStart('0');

        public bool IsZero => IsNumber && Number.Length == 0;

        public bool EqualsSegment(Segment other)
        {
            return IsNumber == other.IsNumber
                && (IsNumber ? Number == other.Number : Text == other.Text);
        }

        public int CompareSegment(Segment other)
        {
            // Ruby can't compare a number with a string, and Pod::Version puts the number first.
            if (IsNumber != other.IsNumber)
            {
                return IsNumber ? -1 : 1;
            }

            if (!IsNumber)
            {
                return Math.Sign(string.CompareOrdinal(Text, other.Text));
            }

            var length = Number.Length.CompareTo(other.Number.Length);
            return length != 0 ? length : Math.Sign(string.CompareOrdinal(Number, other.Number));
        }
    }
}
