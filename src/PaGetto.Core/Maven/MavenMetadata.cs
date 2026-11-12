using System;
using System.Collections.Generic;

namespace PaGetto.Core.Maven;

/// <summary>
/// The content of the <c>maven-metadata.xml</c> of an artifact (its versions) or of a group (the
/// prefixes of its Maven plugins), as read from a mirror.
/// </summary>
public sealed class MavenMetadata
{
    public string GroupId { get; init; }
    public string ArtifactId { get; init; }

    public IReadOnlyList<string> Versions { get; init; } = [];

    public IReadOnlyList<Plugin> Plugins { get; init; } = [];

    /// <summary>
    /// When the metadata last changed, or null.
    /// </summary>
    public DateTime? LastUpdated { get; init; }

    /// <summary>
    /// A Maven plugin of a group, which <c>mvn {prefix}:{goal}</c> finds by its prefix.
    /// </summary>
    public sealed class Plugin
    {
        public string Name { get; init; }
        public string Prefix { get; init; }
        public string ArtifactId { get; init; }
    }
}
