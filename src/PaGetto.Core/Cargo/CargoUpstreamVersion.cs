using PaGetto.Core.Configuration;

namespace PaGetto.Core.Cargo;

/// <summary>
/// A crate version as the index of an upstream registry lists it.
/// </summary>
public sealed class CargoUpstreamVersion
{
    /// <summary>
    /// The crate's name as the upstream index has it.
    /// </summary>
    public string Name { get; init; }

    public string Version { get; init; }

    /// <summary>
    /// The lowercase hex SHA-256 of the <c>.crate</c> file.
    /// </summary>
    public string Cksum { get; init; }

    public bool Yanked { get; init; }

    /// <summary>
    /// The version's line of the upstream index, which is its manifest (see <see cref="CargoIndexEntry"/>).
    /// </summary>
    public string IndexLineJson { get; init; }

    public string DownloadUrl { get; init; }

    /// <summary>
    /// The mirror that listed the version.
    /// </summary>
    public MirrorOptions Mirror { get; init; }
}
