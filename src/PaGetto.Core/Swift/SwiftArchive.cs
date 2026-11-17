using System.Collections.Generic;

namespace PaGetto.Core.Swift;

/// <summary>
/// What <see cref="SwiftArchiveReader"/> reads from a source archive.
/// </summary>
public sealed class SwiftArchive
{
    /// <summary>
    /// The manifests by file name: <c>Package.swift</c>, and the version-specific manifests such as
    /// <c>Package@swift-5.7.swift</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Manifests { get; init; }

    /// <summary>
    /// The readme, or null if the package has none.
    /// </summary>
    public string Readme { get; init; }
}
