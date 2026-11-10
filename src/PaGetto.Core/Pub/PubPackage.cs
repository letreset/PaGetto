using System.Collections.Generic;
using System.Linq;

namespace PaGetto.Core.Pub;

/// <summary>
/// A pub package with the versions a feed serves: its own, and those of its mirrors.
/// </summary>
public sealed class PubPackage
{
    public PubPackage(string name, IReadOnlyList<PubPackageVersion> versions)
    {
        Name = name;
        Versions = versions;

        // Like pub.dev: the newest stable version, or the newest prerelease when there is no stable
        // one. Retracted versions only count when nothing else is left.
        var candidates = versions.Where(v => !v.Retracted).ToList();
        if (candidates.Count == 0)
        {
            candidates = versions.ToList();
        }

        Latest = candidates.LastOrDefault(v => !v.IsPrerelease) ?? candidates.Last();
    }

    public string Name { get; }

    /// <summary>
    /// The versions, oldest first. Never empty.
    /// </summary>
    public IReadOnlyList<PubPackageVersion> Versions { get; }

    public PubPackageVersion Latest { get; }
}
