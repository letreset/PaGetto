using System.Collections.Generic;

namespace PaGetto.Core.PyPI;

/// <summary>
/// A project of a PyPI feed as its simple index page lists it: the files the feed has, combined
/// with those of its mirror.
/// </summary>
public sealed class PyPIProject
{
    /// <summary>
    /// The normalized name.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The versions of the files, lowest first.
    /// </summary>
    public IReadOnlyList<string> Versions { get; init; }

    /// <summary>
    /// The files, ordered by version and then by name.
    /// </summary>
    public IReadOnlyList<PyPIProjectFile> Files { get; init; }
}
