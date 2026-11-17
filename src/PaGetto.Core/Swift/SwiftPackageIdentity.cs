using System.Text.RegularExpressions;

namespace PaGetto.Core.Swift;

/// <summary>
/// A package identifier of the Swift package registry API: <c>scope.name</c>, such as
/// <c>mona.LinkedList</c>. Identifiers are compared regardless of case, so a package is stored
/// under its lowercased identifier, and the case it was first published with is kept in its
/// manifest.
/// See: https://github.com/swiftlang/swift-package-manager/blob/main/Documentation/PackageRegistry/Registry.md#3-conventions
/// </summary>
public static partial class SwiftPackageIdentity
{
    /// <summary>
    /// The longest scope: like a GitHub user or organization name.
    /// </summary>
    public const int MaxScopeLength = 39;

    /// <summary>
    /// The longest package name.
    /// </summary>
    public const int MaxNameLength = 100;

    /// <summary>
    /// Whether a scope is valid: letters, digits and single hyphens between them.
    /// </summary>
    public static bool IsValidScope(string scope)
    {
        return scope != null && ScopeRegex().IsMatch(scope);
    }

    /// <summary>
    /// Whether a package name is valid: letters, digits and single hyphens or underscores between them.
    /// </summary>
    public static bool IsValidName(string name)
    {
        return name != null && NameRegex().IsMatch(name);
    }

    /// <summary>
    /// Splits an identifier into its scope and name.
    /// </summary>
    /// <returns>False if the identifier is not a valid <c>scope.name</c>.</returns>
    public static bool TryParse(string identifier, out string scope, out string name)
    {
        scope = null;
        name = null;

        var dot = identifier?.IndexOf('.') ?? -1;
        if (dot < 0 || !IsValidScope(identifier[..dot]) || !IsValidName(identifier[(dot + 1)..]))
        {
            return false;
        }

        scope = identifier[..dot];
        name = identifier[(dot + 1)..];
        return true;
    }

    /// <summary>
    /// The identifier a package is stored under: <c>scope.name</c>, lowercased.
    /// </summary>
    public static string Normalize(string scope, string name)
    {
        return $"{scope}.{name}".ToLowerInvariant();
    }

    [GeneratedRegex(@"\A[a-zA-Z0-9](?:[a-zA-Z0-9]|-(?=[a-zA-Z0-9])){0,38}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ScopeRegex();

    [GeneratedRegex(@"\A[a-zA-Z0-9](?:[a-zA-Z0-9]|[-_](?=[a-zA-Z0-9])){0,99}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();
}
