using System.Collections.Generic;
using PaGetto.Core.Configuration;

namespace PaGetto.Core.CocoaPods;

/// <summary>
/// A pod in the index of a mirror of a CocoaPods feed.
/// </summary>
public sealed class CocoaPodsUpstreamPod
{
    /// <summary>
    /// The pod name, as the mirror lists it.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The versions, as the mirror lists them.
    /// </summary>
    public IReadOnlyList<string> Versions { get; init; }

    /// <summary>
    /// The mirror that lists the pod.
    /// </summary>
    public MirrorOptions Mirror { get; init; }
}
