using System.Text.RegularExpressions;

namespace PaGetto.Core.Hex;

/// <summary>
/// The names of Hex packages. Hex only accepts lowercase names, so a name is stored as it is.
/// </summary>
public static partial class HexPackageName
{
    /// <summary>
    /// The longest package name accepted. It matches the database column.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Whether <paramref name="name"/> is a package name as hex.pm accepts it: lowercase ASCII
    /// letters, digits and <c>_</c>, starting with a letter. Names are used in URLs and storage
    /// paths, so nothing else is let through.
    /// </summary>
    public static bool IsValid(string name)
    {
        return !string.IsNullOrEmpty(name)
            && name.Length <= MaxLength
            && NameRegex().IsMatch(name);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();
}
