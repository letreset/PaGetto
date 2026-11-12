using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace PaGetto.Core.PyPI;

/// <summary>
/// A Python package version as PEP 440 defines it: <c>[N!]N(.N)*[{a|b|rc}N][.postN][.devN][+local]</c>,
/// with the alternative spellings it allows (<c>1.0-alpha.1</c>, <c>1.0c1</c>, <c>1.0-1</c>, a
/// leading <c>v</c>), ordered the way pip orders versions.
/// See: https://packaging.python.org/en/latest/specifications/version-specifiers/
/// </summary>
public sealed partial class PyPIVersion : IComparable<PyPIVersion>
{
    // Longer strings are not versions; the database column is as long.
    private const int MaxLength = 128;

    // The pre-release labels in their order.
    private static readonly string[] PreLabels = ["a", "b", "rc"];

    private readonly BigInteger _epoch;
    private readonly BigInteger[] _release;
    private readonly int _preLabel;
    private readonly BigInteger? _pre;
    private readonly BigInteger? _post;
    private readonly BigInteger? _dev;
    private readonly string[] _local;

    private PyPIVersion(Match match)
    {
        _epoch = match.Groups["epoch"].Success ? BigInteger.Parse(match.Groups["epoch"].Value, CultureInfo.InvariantCulture) : BigInteger.Zero;
        _release = match.Groups["release"].Value.Split('.').Select(p => BigInteger.Parse(p, CultureInfo.InvariantCulture)).ToArray();

        if (match.Groups["prel"].Success)
        {
            _preLabel = match.Groups["prel"].Value.ToLowerInvariant() switch
            {
                "alpha" or "a" => 0,
                "beta" or "b" => 1,
                _ => 2,
            };
            _pre = Number(match.Groups["pren"]);
        }

        if (match.Groups["postn1"].Success)
        {
            _post = Number(match.Groups["postn1"]);
        }
        else if (match.Groups["postl"].Success)
        {
            _post = Number(match.Groups["postn2"]);
        }

        if (match.Groups["devl"].Success)
        {
            _dev = Number(match.Groups["devn"]);
        }

        _local = match.Groups["local"].Success
            ? match.Groups["local"].Value.ToLowerInvariant().Split('.', '-', '_')
            : null;

        Value = Format();
    }

    /// <summary>
    /// Compares version strings in PEP 440 order, lowest first. Strings that are not versions come
    /// before every version.
    /// </summary>
    public static IComparer<string> StringComparer { get; } = Comparer<string>.Create(CompareStrings);

    /// <summary>
    /// The normalized form, e.g. <c>1.0rc1</c> for <c>1.0-RC.1</c>. Versions are stored in this form.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Whether this is a pre-release or a development release, which installers skip unless asked.
    /// </summary>
    public bool IsPrerelease => _pre.HasValue || _dev.HasValue;

    public static bool TryParse(string version, out PyPIVersion result)
    {
        result = null;
        if (string.IsNullOrEmpty(version) || version.Length > MaxLength)
        {
            return false;
        }

        var match = VersionRegex().Match(version);
        if (!match.Success)
        {
            return false;
        }

        result = new PyPIVersion(match);
        return true;
    }

    /// <exception cref="FormatException">The string is not a PEP 440 version.</exception>
    public static PyPIVersion Parse(string version)
    {
        return TryParse(version, out var result)
            ? result
            : throw new FormatException($"'{version}' is not a PEP 440 version.");
    }

    /// <summary>
    /// The normalized form of a version string, or null if it is not a version.
    /// </summary>
    public static string NormalizeOrNull(string version)
    {
        return TryParse(version, out var result) ? result.Value : null;
    }

    public int CompareTo(PyPIVersion other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = _epoch.CompareTo(other._epoch);
        if (result != 0)
        {
            return result;
        }

        // Trailing zeros don't count: 1.0 == 1.0.0.
        result = CompareRelease(_release, other._release);
        if (result != 0)
        {
            return result;
        }

        result = PreKey().CompareTo(other.PreKey());
        if (result != 0)
        {
            return result;
        }

        // No post-release comes before any post-release.
        result = CompareOptional(_post, other._post, missingFirst: true);
        if (result != 0)
        {
            return result;
        }

        // No dev release comes after any dev release.
        result = CompareOptional(_dev, other._dev, missingFirst: false);
        if (result != 0)
        {
            return result;
        }

        return CompareLocal(_local, other._local);
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
    /// The pre-release part as a sortable key: a dev release of a final version (1.0.dev1) comes
    /// before its pre-releases (1.0a1), which come before the final version.
    /// </summary>
    private (int Rank, int Label, BigInteger Number) PreKey()
    {
        if (_pre.HasValue)
        {
            return (1, _preLabel, _pre.Value);
        }

        return !_post.HasValue && _dev.HasValue ? (0, 0, BigInteger.Zero) : (2, 0, BigInteger.Zero);
    }

    private static int CompareRelease(BigInteger[] x, BigInteger[] y)
    {
        var length = Math.Max(x.Length, y.Length);
        for (var i = 0; i < length; i++)
        {
            var result = (i < x.Length ? x[i] : BigInteger.Zero).CompareTo(i < y.Length ? y[i] : BigInteger.Zero);
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    private static int CompareOptional(BigInteger? x, BigInteger? y, bool missingFirst)
    {
        if (x.HasValue && y.HasValue)
        {
            return x.Value.CompareTo(y.Value);
        }

        if (x.HasValue == y.HasValue)
        {
            return 0;
        }

        return x.HasValue == missingFirst ? 1 : -1;
    }

    /// <summary>
    /// No local label comes first. Labels are compared segment by segment: numbers numerically and
    /// after any text, text alphabetically, and a label that is a prefix of the other first.
    /// </summary>
    private static int CompareLocal(string[] x, string[] y)
    {
        if (x == null || y == null)
        {
            return x == null ? (y == null ? 0 : -1) : 1;
        }

        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            var xNumeric = BigInteger.TryParse(x[i], NumberStyles.None, CultureInfo.InvariantCulture, out var xNumber);
            var yNumeric = BigInteger.TryParse(y[i], NumberStyles.None, CultureInfo.InvariantCulture, out var yNumber);

            var result = (xNumeric, yNumeric) switch
            {
                (true, true) => xNumber.CompareTo(yNumber),
                (true, false) => 1,
                (false, true) => -1,
                _ => string.CompareOrdinal(x[i], y[i]),
            };

            if (result != 0)
            {
                return result;
            }
        }

        return x.Length.CompareTo(y.Length);
    }

    private static BigInteger Number(Group group)
    {
        return group.Success ? BigInteger.Parse(group.Value, CultureInfo.InvariantCulture) : BigInteger.Zero;
    }

    private string Format()
    {
        var builder = new StringBuilder();

        if (!_epoch.IsZero)
        {
            builder.Append(_epoch.ToString(CultureInfo.InvariantCulture)).Append('!');
        }

        builder.AppendJoin('.', _release.Select(p => p.ToString(CultureInfo.InvariantCulture)));

        if (_pre.HasValue)
        {
            builder.Append(PreLabels[_preLabel]).Append(_pre.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (_post.HasValue)
        {
            builder.Append(".post").Append(_post.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (_dev.HasValue)
        {
            builder.Append(".dev").Append(_dev.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (_local != null)
        {
            builder.Append('+').AppendJoin('.', _local);
        }

        return builder.ToString();
    }

    // The regular expression of PEP 440's appendix, which accepts every spelling it normalizes.
    [GeneratedRegex(
        @"^\s*v?(?:(?<epoch>[0-9]+)!)?(?<release>[0-9]+(?:\.[0-9]+)*)"
        + @"(?:[-_.]?(?<prel>alpha|a|beta|b|preview|pre|c|rc)[-_.]?(?<pren>[0-9]+)?)?"
        + @"(?:-(?<postn1>[0-9]+)|[-_.]?(?<postl>post|rev|r)[-_.]?(?<postn2>[0-9]+)?)?"
        + @"(?:[-_.]?(?<devl>dev)[-_.]?(?<devn>[0-9]+)?)?"
        + @"(?:\+(?<local>[a-z0-9]+(?:[-_.][a-z0-9]+)*))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex VersionRegex();
}
