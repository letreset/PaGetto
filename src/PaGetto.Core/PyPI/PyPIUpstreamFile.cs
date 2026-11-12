using System;
using PaGetto.Core.Configuration;

namespace PaGetto.Core.PyPI;

/// <summary>
/// A file that the simple index of a mirror lists for a project.
/// </summary>
public sealed class PyPIUpstreamFile
{
    public string FileName { get; init; }

    /// <summary>
    /// The normalized version, read from the file name.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// The absolute URL the file is downloaded from.
    /// </summary>
    public string Url { get; init; }

    /// <summary>
    /// The lowercase hex SHA-256 the mirror lists, or null.
    /// </summary>
    public string Sha256 { get; init; }

    public string RequiresPython { get; init; }
    public bool Yanked { get; init; }
    public string YankedReason { get; init; }
    public long? Size { get; init; }
    public DateTime? UploadTime { get; init; }

    /// <summary>
    /// The mirror that lists the file.
    /// </summary>
    public MirrorOptions Mirror { get; init; }
}
