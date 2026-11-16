using System.Text.RegularExpressions;

namespace PaGetto.Core.Composer;

/// <summary>
/// The names of Composer packages: <c>vendor/package</c>. Composer compares names regardless of
/// case and asks for them lowercased, so a name is stored lowercased.
/// </summary>
public static partial class ComposerPackageName
{
    /// <summary>
    /// The longest package name accepted. It matches the database column.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Whether <paramref name="name"/> is a package name as Composer validates it: a vendor and a
    /// package name of ASCII letters and digits, separated by single <c>_</c>, <c>.</c> or
    /// <c>-</c> (and <c>--</c> in the package name). Names are used in URLs and storage paths, so
    /// nothing else is let through.
    /// </summary>
    public static bool IsValid(string name)
    {
        return !string.IsNullOrEmpty(name)
            && name.Length <= MaxLength
            && NameRegex().IsMatch(name);
    }

    // Composer's ValidatingArrayLoader.
    [GeneratedRegex("^[a-z0-9]([_.-]?[a-z0-9]+)*/[a-z0-9](([_.]|-{1,2})?[a-z0-9]+)*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();
}
