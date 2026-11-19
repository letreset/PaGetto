using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PaGetto.Core.VsCode;

/// <summary>
/// The identifier of a VS Code extension: <c>publisher.name</c>, from the <c>Publisher</c> and
/// <c>Id</c> of the <c>Identity</c> in its <c>extension.vsixmanifest</c>, such as
/// <c>contoso.hello-world</c>. VS Code matches identifiers regardless of case, so an extension is
/// stored under its lowercased identifier and keeps its published case in its manifest. This
/// server accepts letters, digits, dashes and underscores in each part, starting with a letter or
/// digit, which keeps the identifier safe in URLs and storage paths.
/// </summary>
public static partial class VsCodeExtensionId
{
    /// <summary>
    /// The longest identifier accepted. It matches the database column.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Whether a publisher name or an extension name is valid.
    /// </summary>
    public static bool IsValidPart(string part)
    {
        return !string.IsNullOrEmpty(part) && part.Length < MaxLength && PartRegex().IsMatch(part);
    }

    /// <summary>
    /// The identifier of an extension.
    /// </summary>
    /// <returns>The identifier, or null if a part is not valid or the identifier is too long.</returns>
    public static string Create(string publisher, string name)
    {
        if (!IsValidPart(publisher) || !IsValidPart(name))
        {
            return null;
        }

        var id = publisher + "." + name;
        return id.Length <= MaxLength ? id : null;
    }

    /// <summary>
    /// Splits an identifier into its publisher and name.
    /// </summary>
    /// <returns>False if it is not a valid identifier.</returns>
    public static bool TryParse(string id, out string publisher, out string name)
    {
        publisher = null;
        name = null;

        var dot = id?.IndexOf('.') ?? -1;
        if (dot < 0 || Create(id[..dot], id[(dot + 1)..]) == null)
        {
            return false;
        }

        publisher = id[..dot];
        name = id[(dot + 1)..];
        return true;
    }

    /// <summary>
    /// The name an extension is stored under: its lowercased identifier.
    /// </summary>
    public static string Normalize(string id)
    {
        return id.ToLowerInvariant();
    }

    /// <summary>
    /// A stable UUID for a name, such as an extension's identifier or a publisher, which the gallery
    /// API gives every extension and publisher. The same name has the same UUID on every server.
    /// </summary>
    public static Guid GetUuid(string kind, string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"vscode:{kind}:{name.ToLowerInvariant()}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PartRegex();
}
