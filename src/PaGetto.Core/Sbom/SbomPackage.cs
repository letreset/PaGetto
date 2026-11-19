using System.Collections.Generic;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Sbom;

/// <summary>
/// What a software bill of materials says about a stored package version, read from the manifest
/// the server stored with it.
/// </summary>
public sealed class SbomPackage
{
    /// <summary>
    /// The ecosystem of the package, which decides its package URL.
    /// </summary>
    public FeedType FeedType { get; init; }

    /// <summary>
    /// The name as published, e.g. <c>@scope/name</c>, <c>org.example:lib</c> or a Go module path.
    /// </summary>
    public string Name { get; init; }

    public string Version { get; init; }

    public string Description { get; init; }

    /// <summary>
    /// The license expression or the license names the manifest declares.
    /// </summary>
    public IReadOnlyList<string> Licenses { get; init; } = [];

    /// <summary>
    /// The lowercase hex SHA-256 of the version's archive, or null.
    /// </summary>
    public string Sha256 { get; init; }

    /// <summary>
    /// The files of a version made of several, with their checksums.
    /// </summary>
    public IReadOnlyList<ArtifactFile> Files { get; init; } = [];

    /// <summary>
    /// The dependencies the manifest declares.
    /// </summary>
    public IReadOnlyList<SbomDependency> Dependencies { get; init; } = [];
}
