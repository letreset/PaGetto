using System.Text.Json.Nodes;

namespace PaGetto.Core.Pub;

/// <summary>
/// What <see cref="PubArchiveReader"/> reads from a package archive.
/// </summary>
public sealed class PubArchive
{
    public string Name { get; init; }
    public PubVersion Version { get; init; }

    /// <summary>
    /// The archive's <c>pubspec.yaml</c> as JSON.
    /// </summary>
    public JsonObject Pubspec { get; init; }

    /// <summary>
    /// The content of the archive's <c>README.md</c>, or null if it has none.
    /// </summary>
    public string Readme { get; init; }
}
