using System.Collections.Generic;
using PaGetto.Core.Cargo;

namespace PaGetto.Core.Hex;

/// <summary>
/// A Hex package version: semantic versioning 2.0, as Elixir's <c>Version</c> and Hex read it.
/// The rules and the order are the same as those of crate versions, so this wraps a
/// <see cref="CargoVersion"/>. Like on hex.pm, a feed has one version per version without its
/// build metadata (see <see cref="Precedence"/>).
/// </summary>
public sealed class HexVersion
{
    private readonly CargoVersion _version;

    private HexVersion(CargoVersion version)
    {
        _version = version;
    }

    public bool IsPrerelease => _version.IsPrerelease;

    /// <summary>
    /// The version without its build metadata, e.g. <c>1.0.0</c> for <c>1.0.0+abc</c>.
    /// </summary>
    public string Precedence => _version.Precedence;

    /// <summary>
    /// Compares version strings in Hex's order. Strings that are not versions come first.
    /// </summary>
    public static IComparer<string> StringComparer => CargoVersion.StringComparer;

    public static bool TryParse(string text, out HexVersion version)
    {
        version = CargoVersion.TryParse(text, out var parsed) ? new HexVersion(parsed) : null;
        return version != null;
    }

    public override string ToString()
    {
        return _version.ToString();
    }
}
