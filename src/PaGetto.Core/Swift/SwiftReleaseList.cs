using System.Collections.Generic;

namespace PaGetto.Core.Swift;

/// <summary>
/// The releases of a package of a Swift feed, those published to it and those of its mirrors.
/// </summary>
public sealed class SwiftReleaseList
{
    /// <summary>
    /// The identifier as published, e.g. <c>mona.LinkedList</c>.
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// The versions, newest first.
    /// </summary>
    public IReadOnlyList<string> Versions { get; init; }
}
