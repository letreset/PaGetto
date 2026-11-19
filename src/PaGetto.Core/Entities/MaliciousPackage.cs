using System;

namespace PaGetto.Core.Entities;

/// <summary>
/// A package of a public registry that the OpenSSF malicious packages data lists as malicious: one
/// package of a <c>MAL-</c> advisory of the OSV database. Mirrored versions it affects are neither
/// stored nor served (see <see cref="MaliciousPackages.MaliciousPackageGuard"/>).
/// </summary>
public class MaliciousPackage
{
    public int Key { get; set; }

    /// <summary>
    /// The OSV ecosystem, e.g. <c>npm</c>.
    /// </summary>
    public string Ecosystem { get; set; }

    /// <summary>
    /// The package name, normalized by <see cref="MaliciousPackages.MaliciousPackageName.Normalize"/>.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The advisory's id, e.g. <c>MAL-2024-1234</c>.
    /// </summary>
    public string AdvisoryId { get; set; }

    public string Summary { get; set; }

    /// <summary>
    /// The affected versions as <see cref="MaliciousPackages.MaliciousVersions"/> writes them, or
    /// null when every version is affected.
    /// </summary>
    public string AffectedVersions { get; set; }

    /// <summary>
    /// When the advisory was last changed.
    /// </summary>
    public DateTime ModifiedUtc { get; set; }
}
