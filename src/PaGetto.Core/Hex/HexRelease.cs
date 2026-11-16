using System;
using System.Collections.Generic;

namespace PaGetto.Core.Hex;

/// <summary>
/// A version of a Hex package as the registry lists it, from the feed or from one of its mirrors.
/// </summary>
public sealed class HexRelease
{
    public string Version { get; init; }

    /// <summary>
    /// The SHA-256 of the package's version, metadata and contents (the tarball's <c>CHECKSUM</c>).
    /// </summary>
    public byte[] InnerChecksum { get; init; }

    /// <summary>
    /// The SHA-256 of the tarball.
    /// </summary>
    public byte[] OuterChecksum { get; init; }

    public IReadOnlyList<HexRequirement> Requirements { get; init; } = [];

    /// <summary>
    /// Why the version was retired, or null when it is not retired.
    /// </summary>
    public HexRetirement Retirement { get; init; }

    /// <summary>
    /// When the version was published, or null when that is not known.
    /// </summary>
    public DateTime? PublishedAt { get; init; }
}
