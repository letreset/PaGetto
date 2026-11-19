using System;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Quarantine;

/// <summary>
/// A quarantined version, as the admin page lists it.
/// </summary>
/// <param name="IsNuGet">Whether it is a NuGet package; otherwise an artifact version.</param>
/// <param name="Key">The key of its row.</param>
/// <param name="Feed">Its feed.</param>
/// <param name="Name">The package's stored name.</param>
/// <param name="Version">The version.</param>
/// <param name="Reason">Why it is quarantined.</param>
/// <param name="QuarantinedAtUtc">When it was quarantined.</param>
/// <param name="CachedFrom">The mirror it came from, or null for a published version.</param>
public record QuarantinedVersion(
    bool IsNuGet,
    int Key,
    Feed Feed,
    string Name,
    string Version,
    string Reason,
    DateTime QuarantinedAtUtc,
    string CachedFrom);
