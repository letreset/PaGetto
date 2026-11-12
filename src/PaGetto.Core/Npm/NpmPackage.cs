using System.Collections.Generic;

namespace PaGetto.Core.Npm;

/// <summary>
/// An npm package with the versions and tags a feed serves: its own, and those of its mirrors.
/// </summary>
internal sealed class NpmPackage
{
    /// <summary>
    /// The versions, oldest first. Never empty.
    /// </summary>
    public IReadOnlyList<NpmPackageVersion> Versions { get; init; }

    /// <summary>
    /// The dist-tags, each pointing at one of <see cref="Versions"/>. Always has <c>latest</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> DistTags { get; init; }
}
