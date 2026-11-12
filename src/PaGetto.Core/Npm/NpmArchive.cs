using System.Text.Json.Nodes;

namespace PaGetto.Core.Npm;

/// <summary>
/// What <see cref="NpmArchiveReader"/> reads from a package tarball.
/// </summary>
public sealed class NpmArchive
{
    public string Name { get; init; }
    public NpmVersion Version { get; init; }

    /// <summary>
    /// The tarball's <c>package.json</c>.
    /// </summary>
    public JsonObject Manifest { get; init; }

    /// <summary>
    /// The content of the tarball's readme, or null if it has none.
    /// </summary>
    public string Readme { get; init; }
}
