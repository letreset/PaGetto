using System.Text.RegularExpressions;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// The names of gems. A gem is stored under its lowercased name, so names that differ only in case
/// are one gem, as on RubyGems.org. The name as published is in the version's manifest.
/// </summary>
public static partial class RubyGemsPackageName
{
    /// <summary>
    /// The longest name accepted, the length of the name column.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Whether a name is one <c>gem build</c> accepts: letters, digits, '.', '-' and '_', with at
    /// least one letter.
    /// </summary>
    public static bool IsValid(string name)
    {
        return name != null && name.Length <= MaxLength && NamePattern().IsMatch(name);
    }

    /// <summary>
    /// The name a gem is stored under.
    /// </summary>
    public static string Normalize(string name)
    {
        return name.ToLowerInvariant();
    }

    [GeneratedRegex(@"\A[A-Za-z0-9._\-]*[A-Za-z][A-Za-z0-9._\-]*\z")]
    private static partial Regex NamePattern();
}
