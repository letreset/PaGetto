using System.Text.Json.Nodes;

namespace PaGetto.Core.PyPI;

/// <summary>
/// What <see cref="PyPIArchiveReader"/> reads from a wheel or sdist.
/// </summary>
public sealed class PyPIArchive
{
    /// <summary>
    /// The core metadata file as it is in the archive: <c>METADATA</c> of a wheel, <c>PKG-INFO</c>
    /// of an sdist. A wheel's is served next to it (PEP 658).
    /// </summary>
    public byte[] Metadata { get; init; }

    /// <summary>
    /// The core metadata as JSON, without the description (see <see cref="PyPICoreMetadata"/>).
    /// </summary>
    public JsonObject Manifest { get; init; }

    /// <summary>
    /// The long description, or null if there is none.
    /// </summary>
    public string Readme { get; init; }
}
