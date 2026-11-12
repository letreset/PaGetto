using System;
using System.Text.Json.Nodes;

namespace PaGetto.Core.Npm;

/// <summary>
/// One version of an <see cref="NpmPackage"/>.
/// </summary>
internal sealed class NpmPackageVersion
{
    public string Version { get; init; }

    /// <summary>
    /// The version's manifest, or null if it could not be read.
    /// </summary>
    public JsonObject Manifest { get; init; }

    /// <summary>
    /// False for a deprecated version.
    /// </summary>
    public bool Listed { get; init; }

    public DateTime? Published { get; init; }
}
