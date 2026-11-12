using System;
using System.Collections.Generic;

namespace PaGetto.Core.Entities;

/// <summary>
/// A package of a feed whose type is not <see cref="FeedType.NuGet"/>. Its versions are
/// <see cref="ArtifactVersion"/> rows. The model is the same for every such ecosystem: what is
/// specific to one (the manifest) is kept as JSON on the version.
/// </summary>
public class Artifact
{
    public int Key { get; set; }

    public Guid FeedId { get; set; }
    public Feed Feed { get; set; }

    /// <summary>
    /// The package name, normalized by its ecosystem so that it is unique within the feed.
    /// </summary>
    public string Name { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public List<ArtifactVersion> Versions { get; set; }

    /// <summary>
    /// Names that point at one of the versions, such as the npm dist-tags <c>latest</c> and <c>next</c>.
    /// </summary>
    public List<ArtifactTag> Tags { get; set; }
}
