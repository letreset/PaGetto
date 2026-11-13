namespace PaGetto.Core.Cargo;

/// <summary>
/// One version of a crate's index file, from the feed or from one of its mirrors.
/// </summary>
public sealed class CargoIndexVersion
{
    public string Version { get; init; }

    /// <summary>
    /// The version's manifest (see <see cref="CargoIndexEntry"/>).
    /// </summary>
    public string ManifestJson { get; init; }

    /// <summary>
    /// The lowercase hex SHA-256 of the <c>.crate</c> file.
    /// </summary>
    public string Cksum { get; init; }

    public bool Yanked { get; init; }
}
