using System;

namespace PaGetto.Core.PyPI;

/// <summary>
/// One file of a <see cref="PyPIProject"/>, with what the simple index says about it.
/// </summary>
public sealed class PyPIProjectFile
{
    public string FileName { get; init; }

    /// <summary>
    /// The normalized version the file belongs to.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// The lowercase hex SHA-256 of the file, or null if a mirror lists none.
    /// </summary>
    public string Sha256 { get; init; }

    /// <summary>
    /// The length in bytes, or null if a mirror doesn't say.
    /// </summary>
    public long? Size { get; init; }

    /// <summary>
    /// The Python versions the file supports, e.g. <c>&gt;=3.9</c>, or null.
    /// </summary>
    public string RequiresPython { get; init; }

    /// <summary>
    /// Whether the file is yanked (PEP 592): installers only pick it when it is pinned exactly.
    /// </summary>
    public bool Yanked { get; init; }

    /// <summary>
    /// Why the file was yanked, or null if no reason was given.
    /// </summary>
    public string YankedReason { get; init; }

    /// <summary>
    /// The lowercase hex SHA-256 of the core metadata that the feed serves next to the file
    /// (PEP 658), or null if it serves none.
    /// </summary>
    public string CoreMetadataSha256 { get; init; }

    /// <summary>
    /// When the file was uploaded, or null if a mirror doesn't say.
    /// </summary>
    public DateTime? UploadTime { get; init; }
}
