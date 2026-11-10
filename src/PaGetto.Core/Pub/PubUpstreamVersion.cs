using System;
using PaGetto.Core.Configuration;

namespace PaGetto.Core.Pub;

/// <summary>
/// A package version as an upstream pub server lists it.
/// </summary>
public sealed class PubUpstreamVersion
{
    public string Version { get; init; }
    public string ArchiveUrl { get; init; }

    /// <summary>
    /// The hex SHA-256 of the archive, or null if the upstream does not report one.
    /// </summary>
    public string ArchiveSha256 { get; init; }

    /// <summary>
    /// The version's pubspec, as the JSON object the upstream returned.
    /// </summary>
    public string PubspecJson { get; init; }

    public bool Retracted { get; init; }
    public DateTime? Published { get; init; }

    /// <summary>
    /// The mirror that listed the version.
    /// </summary>
    public MirrorOptions Mirror { get; init; }
}
