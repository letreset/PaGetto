using System.Collections.Generic;

namespace PaGetto.Core.Cran;

/// <summary>
/// What <see cref="CranArchiveReader"/> reads from a source package.
/// </summary>
public sealed class CranArchive
{
    /// <summary>
    /// The package name, from <c>DESCRIPTION</c>.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The version, from <c>DESCRIPTION</c>.
    /// </summary>
    public CranVersion Version { get; init; }

    /// <summary>
    /// The fields of the package's <c>DESCRIPTION</c> file, in its order.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Description { get; init; }

    /// <summary>
    /// The package's <c>README.md</c>, or null if it has none.
    /// </summary>
    public string Readme { get; init; }
}
