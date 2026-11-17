using System.Text.RegularExpressions;

namespace PaGetto.Core.Cran;

/// <summary>
/// The names of R packages. R matches names by case, but CRAN refuses a name that differs from
/// another only in case, so a package is stored under its lowercased name and keeps the case it
/// was published with in its manifest.
/// See: https://cran.r-project.org/doc/manuals/r-release/R-exts.html#The-DESCRIPTION-file
/// </summary>
public static partial class CranPackageName
{
    /// <summary>
    /// The longest package name accepted. It matches the database column.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Whether <paramref name="name"/> is a package name as R accepts it: ASCII letters, digits and
    /// dots, at least two characters, starting with a letter and not ending with a dot.
    /// </summary>
    public static bool IsValid(string name)
    {
        return !string.IsNullOrEmpty(name)
            && name.Length <= MaxLength
            && NameRegex().IsMatch(name);
    }

    /// <summary>
    /// The name a package is stored under.
    /// </summary>
    public static string Normalize(string name)
    {
        return name.ToLowerInvariant();
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9.]*[A-Za-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();
}
