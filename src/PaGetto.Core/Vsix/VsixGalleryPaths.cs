namespace PaGetto.Core.Vsix;

/// <summary>
/// The paths of a VSIX feed's gallery, below its URL (<c>https://server/feeds/{slug}/vsix</c>).
/// Identifiers and versions only have characters that are safe in a URL path.
/// </summary>
public static class VsixGalleryPaths
{
    /// <summary>
    /// The Atom feed Visual Studio reads.
    /// </summary>
    public const string AtomFeed = "atom.xml";

    /// <summary>
    /// The VSIX file of a version, named as Visual Studio saves it: <c>extensions/{id}/{version}/{id}-{version}.vsix</c>.
    /// </summary>
    public static string GetFilePath(string id, string version)
    {
        return $"extensions/{id}/{version}/{GetFileName(id, version)}";
    }

    public static string GetFileName(string id, string version)
    {
        return $"{id}-{version}.vsix";
    }

    /// <summary>
    /// An image of a version: <c>extensions/{id}/{version}/icon</c> or <c>/preview</c>.
    /// </summary>
    public static string GetImagePath(string id, string version, VsixImageKind kind)
    {
        return $"extensions/{id}/{version}/{(kind == VsixImageKind.Icon ? "icon" : "preview")}";
    }
}
