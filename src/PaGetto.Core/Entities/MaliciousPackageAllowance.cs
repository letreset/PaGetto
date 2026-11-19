using System;

namespace PaGetto.Core.Entities;

/// <summary>
/// A package, or one version of it, that an administrator allows although the malicious packages
/// data lists it, e.g. for a false positive. Applies to every feed of the ecosystem.
/// </summary>
public class MaliciousPackageAllowance
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
    /// The allowed version, or null for every version.
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// Why it is allowed.
    /// </summary>
    public string Reason { get; set; }

    /// <summary>
    /// The username of the administrator who allowed it.
    /// </summary>
    public string CreatedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
