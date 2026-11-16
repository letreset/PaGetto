using System.Text.Json.Nodes;

namespace PaGetto.Core.Composer;

/// <summary>
/// What <see cref="ComposerArchiveReader"/> reads from a package zip.
/// </summary>
public sealed class ComposerArchive
{
    /// <summary>
    /// The package's <c>composer.json</c>.
    /// </summary>
    public JsonObject ComposerJson { get; init; }

    /// <summary>
    /// The package's <c>README.md</c>, or null if it has none.
    /// </summary>
    public string Readme { get; init; }
}
