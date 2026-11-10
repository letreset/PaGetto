namespace PaGetto.Core.Pub;

/// <summary>
/// An entry of a tar archive, as <see cref="PubTarReader"/> reads it.
/// </summary>
internal sealed class PubTarEntry
{
    /// <summary>
    /// The entry's path in the archive, with forward slashes as written.
    /// </summary>
    public string Path { get; init; }

    /// <summary>
    /// The length of the entry's content in bytes.
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// True for a regular file. False for directories, links and other entries.
    /// </summary>
    public bool IsFile { get; init; }
}
