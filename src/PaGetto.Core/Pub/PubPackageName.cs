using System.Text.RegularExpressions;

namespace PaGetto.Core.Pub;

public static partial class PubPackageName
{
    /// <summary>
    /// The longest package name accepted, the same limit as on pub.dev.
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>
    /// Whether <paramref name="name"/> is a package name as pub.dev accepts it: a lowercase Dart
    /// identifier. Names are used in URLs and storage paths, so nothing else is let through.
    /// </summary>
    public static bool IsValid(string name)
    {
        return !string.IsNullOrEmpty(name)
            && name.Length <= MaxLength
            && NameRegex().IsMatch(name);
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();
}
