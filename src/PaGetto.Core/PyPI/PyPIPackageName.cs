using System.Text.RegularExpressions;

namespace PaGetto.Core.PyPI;

/// <summary>
/// The names of Python projects. A name is stored and looked up in its normalized form, so that
/// <c>Foo.Bar</c>, <c>foo_bar</c> and <c>FOO-BAR</c> are one project.
/// See: https://packaging.python.org/en/latest/specifications/name-normalization/
/// </summary>
public static partial class PyPIPackageName
{
    /// <summary>
    /// The longest name accepted, the length of the name column.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Whether a name is a valid project name: ASCII letters, digits, <c>.</c>, <c>_</c> and
    /// <c>-</c>, starting and ending with a letter or digit, and short enough to be stored.
    /// </summary>
    public static bool IsValid(string name)
    {
        return !string.IsNullOrEmpty(name)
            && name.Length <= MaxLength
            && ValidNameRegex().IsMatch(name);
    }

    /// <summary>
    /// The normalized form of a name: lowercase, with each run of <c>.</c>, <c>_</c> and <c>-</c>
    /// replaced by one <c>-</c>.
    /// </summary>
    public static string Normalize(string name)
    {
        return SeparatorsRegex().Replace(name, "-").ToLowerInvariant();
    }

    [GeneratedRegex("^([A-Z0-9]|[A-Z0-9][A-Z0-9._-]*[A-Z0-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ValidNameRegex();

    [GeneratedRegex("[-_.]+", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorsRegex();
}
