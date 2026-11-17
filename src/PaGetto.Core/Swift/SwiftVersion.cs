using System.Collections.Generic;
using PaGetto.Core.Cargo;

namespace PaGetto.Core.Swift;

/// <summary>
/// A Swift package release version: semantic versioning 2.0, which the registry API requires and
/// Swift Package Manager orders by precedence. The rules and the order are the same as those of
/// crate versions, so this wraps a <see cref="CargoVersion"/>. A feed has one release per version
/// without its build metadata (see <see cref="Precedence"/>).
/// </summary>
public sealed class SwiftVersion
{
    private readonly CargoVersion _version;

    private SwiftVersion(CargoVersion version)
    {
        _version = version;
    }

    public bool IsPrerelease => _version.IsPrerelease;

    /// <summary>
    /// The version without its build metadata, e.g. <c>1.0.0</c> for <c>1.0.0+abc</c>.
    /// </summary>
    public string Precedence => _version.Precedence;

    /// <summary>
    /// Compares version strings in Swift Package Manager's order. Strings that are not versions come first.
    /// </summary>
    public static IComparer<string> StringComparer => CargoVersion.StringComparer;

    public static bool TryParse(string text, out SwiftVersion version)
    {
        version = CargoVersion.TryParse(text, out var parsed) ? new SwiftVersion(parsed) : null;
        return version != null;
    }

    public override string ToString()
    {
        return _version.ToString();
    }
}
