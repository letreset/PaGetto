using System;

namespace PaGetto.Core.Audit;

/// <summary>
/// Which audit events to list. Empty values don't filter.
/// </summary>
public class AuditEventFilter
{
    /// <summary>
    /// The exact event name.
    /// </summary>
    public string Event { get; set; }

    /// <summary>
    /// Part of the actor.
    /// </summary>
    public string Actor { get; set; }

    /// <summary>
    /// The exact feed slug.
    /// </summary>
    public string Feed { get; set; }

    /// <summary>
    /// Part of the target or of the package id.
    /// </summary>
    public string Target { get; set; }

    /// <summary>
    /// Events at or after this time.
    /// </summary>
    public DateTime? FromUtc { get; set; }

    /// <summary>
    /// Events before this time.
    /// </summary>
    public DateTime? BeforeUtc { get; set; }
}
