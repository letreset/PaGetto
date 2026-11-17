using System.Collections.Generic;
using PaGetto.Core.Configuration;

namespace PaGetto.Core.Cran;

/// <summary>
/// A version of a package on a mirror of a CRAN feed: the current version from the mirror's
/// <c>PACKAGES</c> index, or an older one from its <c>Archive/</c>.
/// </summary>
public sealed class CranUpstreamVersion
{
    /// <summary>
    /// The package name as the mirror writes it.
    /// </summary>
    public string Name { get; init; }

    public CranVersion Version { get; init; }

    /// <summary>
    /// The path of the source package below the mirror's <c>src/contrib/</c>.
    /// </summary>
    public string Path { get; init; }

    /// <summary>
    /// The fields of the version's entry in the mirror's index, or null for an archived version.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> IndexEntry { get; init; }

    /// <summary>
    /// The MD5 of the source package that the mirror's index lists, or null.
    /// </summary>
    public string Md5 => IndexEntry == null ? null : CranDcf.GetValue(IndexEntry, CranManifest.Md5Field);

    /// <summary>
    /// The mirror that has the version.
    /// </summary>
    public MirrorOptions Mirror { get; init; }
}
