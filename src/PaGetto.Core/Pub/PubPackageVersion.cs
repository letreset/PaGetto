using System;

namespace PaGetto.Core.Pub;

/// <summary>
/// One version of a <see cref="PubPackage"/>.
/// </summary>
public sealed class PubPackageVersion
{
    public string Version { get; init; }
    public bool IsPrerelease { get; init; }
    public bool Retracted { get; init; }

    /// <summary>
    /// The hex SHA-256 of the archive, or null if a mirror does not report one.
    /// </summary>
    public string ArchiveSha256 { get; init; }

    /// <summary>
    /// The version's pubspec as a JSON object.
    /// </summary>
    public string PubspecJson { get; init; }

    public DateTime? Published { get; init; }
}
