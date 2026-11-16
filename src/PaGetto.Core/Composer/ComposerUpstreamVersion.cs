using PaGetto.Core.Configuration;

namespace PaGetto.Core.Composer;

/// <summary>
/// A version of a package in the metadata of a mirror of a Composer feed.
/// </summary>
public sealed class ComposerUpstreamVersion
{
    /// <summary>
    /// The version as the mirror writes it, e.g. <c>v1.2.3</c>.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// The normalized version.
    /// </summary>
    public string NormalizedVersion { get; init; }

    /// <summary>
    /// The version's metadata as the mirror lists it, expanded when the mirror's metadata is minified.
    /// It is kept as text because listings are cached and shared.
    /// </summary>
    public string Json { get; init; }

    /// <summary>
    /// The mirror that lists the version.
    /// </summary>
    public MirrorOptions Mirror { get; init; }
}
