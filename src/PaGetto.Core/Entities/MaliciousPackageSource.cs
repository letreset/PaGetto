using System;

namespace PaGetto.Core.Entities;

/// <summary>
/// The state of the local copy of one ecosystem's malicious packages.
/// </summary>
public class MaliciousPackageSource
{
    /// <summary>
    /// The OSV ecosystem, e.g. <c>npm</c>.
    /// </summary>
    public string Ecosystem { get; set; }

    /// <summary>
    /// The newest change of the OSV data that the copy includes; the next refresh reads the changes after it.
    /// </summary>
    public DateTime? CursorUtc { get; set; }

    /// <summary>
    /// When the copy was last refreshed.
    /// </summary>
    public DateTime? RefreshedAtUtc { get; set; }

    /// <summary>
    /// When the last refresh was tried, whether or not it succeeded.
    /// </summary>
    public DateTime? AttemptedAtUtc { get; set; }

    /// <summary>
    /// Why the last refresh failed, or null if it succeeded.
    /// </summary>
    public string LastError { get; set; }
}
