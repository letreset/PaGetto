using System;
using System.Text.RegularExpressions;

namespace PaGetto.Core.Npm;

/// <summary>
/// The rules for npm package names: an optional scope (<c>@scope/name</c>) and URL-safe characters.
/// Names are used in URLs and storage paths, so nothing else is let through.
/// </summary>
public static partial class NpmPackageName
{
    /// <summary>
    /// The longest package name accepted. npm allows 214 characters; this is the length of the
    /// database column.
    /// </summary>
    public const int MaxLength = 128;

    private const string TarballExtension = ".tgz";

    /// <summary>
    /// Whether <paramref name="name"/> is an npm package name. Uppercase letters are accepted,
    /// because packages published before npm required lowercase names still have them.
    /// </summary>
    public static bool IsValid(string name)
    {
        return !string.IsNullOrEmpty(name)
            && name.Length <= MaxLength
            && NameRegex().IsMatch(name);
    }

    /// <summary>
    /// Whether a package may be published under <paramref name="name"/>: a valid, lowercase name.
    /// </summary>
    public static bool IsValidForPublish(string name)
    {
        return IsValid(name) && string.Equals(name, name.ToLowerInvariant(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The name a package is stored under. npm doesn't allow two packages whose names only differ
    /// in case, so names are stored lowercased.
    /// </summary>
    public static string Normalize(string name)
    {
        return name.ToLowerInvariant();
    }

    /// <summary>
    /// Reads a name from a URL path segment. Clients send a scoped name as <c>@scope%2fname</c>, and
    /// ASP.NET Core leaves the <c>%2f</c> encoded.
    /// </summary>
    public static string FromUrlSegment(string segment)
    {
        return segment?.Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The name as a single URL path segment, as npm clients send it: <c>@scope%2fname</c>.
    /// </summary>
    public static string Escape(string name)
    {
        return Uri.EscapeDataString(name).Replace("%40", "@", StringComparison.Ordinal);
    }

    /// <summary>
    /// The file name of a version's tarball, as the npm registry names it: the name without its
    /// scope, then the version, e.g. <c>name-1.0.0.tgz</c> for <c>@scope/name</c>.
    /// </summary>
    public static string GetTarballFileName(string name, string version)
    {
        return $"{GetUnscopedName(name)}-{version}{TarballExtension}";
    }

    /// <summary>
    /// Reads the version from the file name of a tarball of package <paramref name="name"/>.
    /// </summary>
    public static bool TryGetVersionFromTarballFileName(string name, string fileName, out string version)
    {
        version = null;

        var prefix = GetUnscopedName(name) + "-";
        if (fileName == null
            || !fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(TarballExtension, StringComparison.OrdinalIgnoreCase)
            || fileName.Length <= prefix.Length + TarballExtension.Length)
        {
            return false;
        }

        version = fileName[prefix.Length..^TarballExtension.Length];
        return true;
    }

    private static string GetUnscopedName(string name)
    {
        var slash = name.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? name : name[(slash + 1)..];
    }

    // From validate-npm-package-name, without the "*" it allows in scopes, which no storage path may contain.
    [GeneratedRegex("^(?:@[a-z0-9~-][a-z0-9._~-]*/)?[a-z0-9~-][a-z0-9._~-]*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NameRegex();
}
