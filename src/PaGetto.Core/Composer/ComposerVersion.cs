using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace PaGetto.Core.Composer;

/// <summary>
/// A version of a Composer package, as written (<c>v1.2.3</c>, <c>1.0.0-beta1</c>, <c>dev-main</c>,
/// <c>2.x-dev</c>) with the normalized form Composer compares (<c>1.2.3.0</c>, <c>1.0.0.0-beta1</c>,
/// <c>dev-main</c>, <c>2.9999999.9999999.9999999-dev</c>). Two versions with the same normalized form
/// are the same version.
/// See: https://getcomposer.org/doc/articles/versions.md
/// </summary>
public sealed partial class ComposerVersion
{
    /// <summary>
    /// The longest version accepted. It matches the database column.
    /// </summary>
    public const int MaxLength = 128;

    public const string StableStability = "stable";
    public const string DevStability = "dev";

    private const string BranchPrefix = "dev-";

    // The stability suffix of Composer's version parser.
    private const string Modifier = @"[._-]?(?:(stable|beta|b|RC|alpha|a|patch|pl|p)((?:[.-]?\d+)*)?)?([.-]?dev)?";

    // The forms php's version_compare orders words by; "#" stands for a number. A word is matched by
    // its start, so "patch" is "p". Anything else comes before "dev".
    private static readonly (string Form, int Order)[] _specialForms =
    [
        ("dev", 0), ("alpha", 1), ("a", 1), ("beta", 2), ("b", 2), ("RC", 3), ("rc", 3), ("#", 4), ("pl", 5), ("p", 5),
    ];

    private ComposerVersion(string text, string normalized)
    {
        Text = text;
        Normalized = normalized;
        Stability = GetStability(normalized);
    }

    /// <summary>
    /// The version as written.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// The version as Composer normalizes it, which identifies the version.
    /// </summary>
    public string Normalized { get; }

    /// <summary>
    /// <c>stable</c>, <c>RC</c>, <c>beta</c>, <c>alpha</c> or <c>dev</c>.
    /// </summary>
    public string Stability { get; }

    /// <summary>
    /// Whether this is a development version, such as a branch: <c>dev-main</c> or <c>1.x-dev</c>.
    /// </summary>
    public bool IsDev => Stability == DevStability;

    public bool IsPrerelease => Stability != StableStability;

    /// <summary>
    /// Compares version strings in Composer's order: branches such as <c>dev-main</c> first, then
    /// the others by php's <c>version_compare</c> of their normalized forms. Strings that are not
    /// versions come before both, in ordinal order.
    /// </summary>
    public static IComparer<string> StringComparer { get; } = Comparer<string>.Create(CompareStrings);

    /// <summary>
    /// Parses a version the way Composer's version parser normalizes it.
    /// </summary>
    /// <returns>False if Composer would not accept the version.</returns>
    public static bool TryParse(string text, out ComposerVersion version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        var normalized = Normalize(text);
        if (normalized == null)
        {
            return false;
        }

        version = new ComposerVersion(text, normalized);
        return true;
    }

    /// <summary>
    /// Whether a version can be stored in a feed: it is in the URLs of its archive and in its storage
    /// path, so only ASCII letters, digits and <c>._+-</c> are accepted, and branch names with a slash
    /// are not.
    /// </summary>
    public static bool IsStorable(string text)
    {
        return !string.IsNullOrEmpty(text)
            && text.Length <= MaxLength
            && StorableRegex().IsMatch(text);
    }

    /// <summary>
    /// Compares normalized versions as Composer orders them.
    /// </summary>
    public static int CompareNormalized(string x, string y)
    {
        var xIsBranch = x.StartsWith(BranchPrefix, StringComparison.Ordinal);
        var yIsBranch = y.StartsWith(BranchPrefix, StringComparison.Ordinal);

        if (xIsBranch || yIsBranch)
        {
            return xIsBranch && yIsBranch
                ? string.CompareOrdinal(x, y)
                : xIsBranch ? -1 : 1;
        }

        return PhpVersionCompare(x, y);
    }

    public override string ToString()
    {
        return Text;
    }

    private static int CompareStrings(string x, string y)
    {
        var xIsVersion = TryParse(x, out var xVersion);
        var yIsVersion = TryParse(y, out var yVersion);

        if (!xIsVersion || !yIsVersion)
        {
            return xIsVersion ? 1 : yIsVersion ? -1 : string.CompareOrdinal(x, y);
        }

        var result = CompareNormalized(xVersion.Normalized, yVersion.Normalized);
        return result != 0 ? result : string.CompareOrdinal(x, y);
    }

    /// <summary>
    /// Composer's <c>VersionParser::normalize</c>, without the aliases and stability flags that only
    /// constraints have.
    /// </summary>
    private static string Normalize(string version)
    {
        if (version is "master" or "trunk" or "default")
        {
            version = BranchPrefix + version;
        }

        if (version.StartsWith(BranchPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return BranchPrefix + version[BranchPrefix.Length..];
        }

        var buildMetadata = BuildMetadataRegex().Match(version);
        if (buildMetadata.Success)
        {
            version = buildMetadata.Groups[1].Value;
        }

        string normalized;
        int index;
        Match match;
        if ((match = ClassicalRegex().Match(version)).Success)
        {
            normalized = match.Groups[1].Value
                + OrZero(match.Groups[2])
                + OrZero(match.Groups[3])
                + OrZero(match.Groups[4]);
            index = 5;
        }
        else if ((match = DateRegex().Match(version)).Success)
        {
            normalized = NonDigitRegex().Replace(match.Groups[1].Value, ".");
            index = 2;
        }
        else
        {
            // A numbered branch such as 1.x-dev.
            var devBranch = DevBranchRegex().Match(version);
            if (devBranch.Success)
            {
                var branch = NormalizeBranch(devBranch.Groups[1].Value);
                if (!branch.Contains(BranchPrefix, StringComparison.Ordinal))
                {
                    return branch;
                }
            }

            return null;
        }

        var stability = match.Groups[index].Value;
        if (stability.Length > 0)
        {
            if (stability == StableStability)
            {
                return normalized;
            }

            normalized += "-" + ExpandStability(stability) + match.Groups[index + 1].Value.TrimStart('.', '-');
        }

        if (match.Groups[index + 2].Value.Length > 0)
        {
            normalized += "-dev";
        }

        return normalized;
    }

    private static string OrZero(Group group)
    {
        return group.Value.Length > 0 ? group.Value : ".0";
    }

    private static string ExpandStability(string stability)
    {
        stability = stability.ToLowerInvariant();
        return stability switch
        {
            "a" => "alpha",
            "b" => "beta",
            "p" or "pl" => "patch",
            "rc" => "RC",
            _ => stability,
        };
    }

    private static string NormalizeBranch(string name)
    {
        name = name.Trim();

        var match = NumberedBranchRegex().Match(name);
        if (!match.Success)
        {
            return BranchPrefix + name;
        }

        var version = new StringBuilder();
        for (var i = 1; i < 5; i++)
        {
            version.Append(match.Groups[i].Success ? match.Groups[i].Value.Replace('*', 'x').Replace('X', 'x') : ".x");
        }

        return version.Replace("x", "9999999").Append("-dev").ToString();
    }

    /// <summary>
    /// Composer's <c>VersionParser::parseStability</c>.
    /// </summary>
    private static string GetStability(string normalized)
    {
        if (normalized.StartsWith(BranchPrefix, StringComparison.Ordinal) || normalized.EndsWith("-dev", StringComparison.Ordinal))
        {
            return DevStability;
        }

        var match = StabilityRegex().Match(normalized.ToLowerInvariant());
        if (match.Groups[3].Value.Length > 0)
        {
            return DevStability;
        }

        return match.Groups[1].Value switch
        {
            "beta" or "b" => "beta",
            "alpha" or "a" => "alpha",
            "rc" => "RC",
            _ => StableStability,
        };
    }

    /// <summary>
    /// php's <c>version_compare</c>, which Composer compares normalized versions with.
    /// </summary>
    private static int PhpVersionCompare(string x, string y)
    {
        if (x.Length == 0 || y.Length == 0)
        {
            return x.Length == 0 && y.Length == 0 ? 0 : x.Length == 0 ? -1 : 1;
        }

        var xParts = (x[0] == '#' ? x : Canonicalize(x)).Split('.');
        var yParts = (y[0] == '#' ? y : Canonicalize(y)).Split('.');

        var i = 0;
        for (; i < xParts.Length && i < yParts.Length; i++)
        {
            var result = CompareParts(xParts[i], yParts[i]);
            if (result != 0)
            {
                return result;
            }
        }

        // The longer version wins with a number next, and is compared as a word with a number otherwise:
        // 1.0.0.0 is after 1.0.0.0-beta1 and before 1.0.0.0-patch1.
        if (i < xParts.Length)
        {
            return StartsWithDigit(xParts[i]) ? 1 : PhpVersionCompare(string.Join('.', xParts[i..]), "#N#");
        }

        if (i < yParts.Length)
        {
            return StartsWithDigit(yParts[i]) ? -1 : PhpVersionCompare("#N#", string.Join('.', yParts[i..]));
        }

        return 0;
    }

    private static int CompareParts(string x, string y)
    {
        var xIsNumber = StartsWithDigit(x);
        var yIsNumber = StartsWithDigit(y);

        if (xIsNumber && yIsNumber)
        {
            return BigInteger.Parse(LeadingDigits(x)).CompareTo(BigInteger.Parse(LeadingDigits(y)));
        }

        return GetSpecialFormOrder(xIsNumber ? "#N#" : x).CompareTo(GetSpecialFormOrder(yIsNumber ? "#N#" : y));
    }

    private static int GetSpecialFormOrder(string form)
    {
        foreach (var (name, order) in _specialForms)
        {
            if (form.StartsWith(name, StringComparison.Ordinal))
            {
                return order;
            }
        }

        return -1;
    }

    /// <summary>
    /// php's canonical form of a version: <c>-</c>, <c>_</c> and <c>+</c> become dots, and a dot
    /// separates numbers from words, so <c>1.0.0.0-beta1</c> is <c>1.0.0.0.beta.1</c>.
    /// </summary>
    private static string Canonicalize(string version)
    {
        var result = new StringBuilder(version.Length * 2);
        var previous = version[0];
        result.Append(previous);

        for (var i = 1; i < version.Length; i++)
        {
            var c = version[i];
            if (c is '-' or '_' or '+')
            {
                AppendDot(result);
            }
            else if ((IsNonDigit(previous) && char.IsAsciiDigit(c)) || (char.IsAsciiDigit(previous) && IsNonDigit(c)))
            {
                AppendDot(result);
                result.Append(c);
            }
            else if (!char.IsAsciiLetterOrDigit(c))
            {
                AppendDot(result);
            }
            else
            {
                result.Append(c);
            }

            previous = c;
        }

        return result.ToString();
    }

    private static void AppendDot(StringBuilder version)
    {
        if (version[^1] != '.')
        {
            version.Append('.');
        }
    }

    private static bool IsNonDigit(char c)
    {
        return !char.IsAsciiDigit(c) && c != '.';
    }

    private static bool StartsWithDigit(string part)
    {
        return part.Length > 0 && char.IsAsciiDigit(part[0]);
    }

    private static string LeadingDigits(string part)
    {
        var length = 0;
        while (length < part.Length && char.IsAsciiDigit(part[length]))
        {
            length++;
        }

        return part[..length];
    }

    [GeneratedRegex(@"^v?(\d{1,5})(\.\d+)?(\.\d+)?(\.\d+)?" + Modifier + @"\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClassicalRegex();

    [GeneratedRegex(@"^v?(\d{4}(?:[.:-]?\d{2}){1,6}(?:[.:-]?\d{1,3}){0,2})" + Modifier + @"\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DateRegex();

    [GeneratedRegex(Modifier + @"(?:\+.*)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StabilityRegex();

    [GeneratedRegex(@"^([^,\s+]+)\+[^\s]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex BuildMetadataRegex();

    [GeneratedRegex(@"^(.*?)[.-]?dev\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DevBranchRegex();

    [GeneratedRegex(@"^v?(\d+)(\.(?:\d+|[xX*]))?(\.(?:\d+|[xX*]))?(\.(?:\d+|[xX*]))?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumberedBranchRegex();

    [GeneratedRegex(@"\D", RegexOptions.CultureInvariant)]
    private static partial Regex NonDigitRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex StorableRegex();
}
