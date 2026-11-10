using System.Collections.Generic;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Artifacts;

/// <summary>
/// One page of <see cref="IArtifactDatabase.SearchAsync"/>.
/// </summary>
public class ArtifactSearchResult
{
    /// <summary>
    /// The number of packages that match, across all pages.
    /// </summary>
    public int TotalHits { get; set; }

    /// <summary>
    /// The packages on this page, each with all of its versions.
    /// </summary>
    public IReadOnlyList<Artifact> Artifacts { get; set; }
}
