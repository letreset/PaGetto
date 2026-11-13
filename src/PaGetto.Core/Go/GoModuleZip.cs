namespace PaGetto.Core.Go;

/// <summary>
/// What <see cref="GoModuleZipReader"/> reads from a module zip.
/// </summary>
public sealed class GoModuleZip
{
    /// <summary>
    /// The content of the module's <c>go.mod</c> file, or null if the zip has none.
    /// </summary>
    public string GoMod { get; init; }

    /// <summary>
    /// The content of the module's <c>README.md</c>, or null if the zip has none.
    /// </summary>
    public string Readme { get; init; }

    /// <summary>
    /// Whether the zip has entries for folders, as <c>git archive</c> writes them. The go command's
    /// hash of a zip counts them, but its hash of the extracted module does not, so
    /// <c>go mod verify</c> fails for such a zip.
    /// </summary>
    public bool HasDirectoryEntries { get; init; }
}
