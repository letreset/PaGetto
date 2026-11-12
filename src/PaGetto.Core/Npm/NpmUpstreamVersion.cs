using System;
using System.Collections.Generic;
using PaGetto.Core.Configuration;

namespace PaGetto.Core.Npm;

/// <summary>
/// A package version as an upstream npm registry lists it.
/// </summary>
public sealed class NpmUpstreamVersion
{
    public string Version { get; init; }

    /// <summary>
    /// The version's manifest, as the JSON object the upstream returned.
    /// </summary>
    public string ManifestJson { get; init; }

    public string TarballUrl { get; init; }

    /// <summary>
    /// The <c>dist.integrity</c> of the manifest, or null.
    /// </summary>
    public string Integrity { get; init; }

    /// <summary>
    /// The <c>dist.shasum</c> of the manifest, or null.
    /// </summary>
    public string Shasum { get; init; }

    /// <summary>
    /// The deprecation message, or null if the version is not deprecated.
    /// </summary>
    public string Deprecated { get; init; }

    public DateTime? Published { get; init; }

    /// <summary>
    /// The package's dist-tags on the upstream. Every version of one listing shares them.
    /// </summary>
    public IReadOnlyDictionary<string, string> DistTags { get; init; }

    /// <summary>
    /// The mirror that listed the version.
    /// </summary>
    public MirrorOptions Mirror { get; init; }
}
