using System.Text.RegularExpressions;

namespace PaGetto.Core.Vsix;

/// <summary>
/// The identifier of a Visual Studio extension: the <c>Id</c> of the <c>Identity</c> in its
/// manifest, such as <c>Contoso.Tools.a14874d2-8199-4a60-af8a-11d6447813aa</c>. Visual Studio
/// matches identifiers regardless of case, so an extension is stored under its lowercased
/// identifier and keeps its published case in its manifest. This server accepts letters, digits,
/// dots, dashes and underscores, starting and ending with a letter or digit, which keeps the
/// identifier safe in URLs and storage paths.
/// </summary>
public static partial class VsixExtensionId
{
    /// <summary>
    /// The longest identifier accepted. It matches the database column.
    /// </summary>
    public const int MaxLength = 128;

    public static bool IsValid(string id)
    {
        return !string.IsNullOrEmpty(id) && id.Length <= MaxLength && IdRegex().IsMatch(id);
    }

    /// <summary>
    /// The name an extension is stored under: its lowercased identifier.
    /// </summary>
    public static string Normalize(string id)
    {
        return id.ToLowerInvariant();
    }

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex IdRegex();
}
