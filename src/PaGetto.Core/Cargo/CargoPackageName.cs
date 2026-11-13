using System.Text.RegularExpressions;

namespace PaGetto.Core.Cargo;

/// <summary>
/// The names of crates. Crates are stored under their lowercased name, which is also the name of
/// their index file; the name as published is kept in the manifest.
/// </summary>
public static partial class CargoPackageName
{
    /// <summary>
    /// The longest crate name accepted, the same limit as on crates.io.
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>
    /// Whether <paramref name="name"/> is a crate name as crates.io accepts it: ASCII letters,
    /// digits, <c>-</c> and <c>_</c>, starting with a letter. Names are used in URLs and storage
    /// paths, so nothing else is let through.
    /// </summary>
    public static bool IsValid(string name)
    {
        return !string.IsNullOrEmpty(name)
            && name.Length <= MaxLength
            && NameRegex().IsMatch(name);
    }

    /// <summary>
    /// The path of a crate's index file below the index root, e.g. <c>1/a</c>, <c>3/s/syn</c> or
    /// <c>se/rd/serde</c>. It depends on the length of the lowercased name.
    /// </summary>
    public static string GetIndexPath(string name)
    {
        var lower = name.ToLowerInvariant();

        return $"{GetPrefix(lower)}/{lower}";
    }

    /// <summary>
    /// The folders of a crate's index file, from the name as given, e.g. <c>3/s</c> for <c>syn</c>
    /// or <c>Se/rd</c> for <c>Serde</c>.
    /// </summary>
    public static string GetPrefix(string name)
    {
        return name.Length switch
        {
            1 => "1",
            2 => "2",
            3 => $"3/{name[0]}",
            _ => $"{name[..2]}/{name[2..4]}",
        };
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();
}
