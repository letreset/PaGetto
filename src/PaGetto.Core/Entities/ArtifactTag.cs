namespace PaGetto.Core.Entities;

/// <summary>
/// A named pointer to one version of an <see cref="Artifact"/>, such as an npm dist-tag. It is
/// state of the package, not of a version: moving a tag changes no version.
/// </summary>
public class ArtifactTag
{
    public int Key { get; set; }

    public int ArtifactKey { get; set; }
    public Artifact Artifact { get; set; }

    /// <summary>
    /// The tag, unique within the package, e.g. <c>latest</c>.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The version the tag points at, as it was published.
    /// </summary>
    public string Version { get; set; }
}
