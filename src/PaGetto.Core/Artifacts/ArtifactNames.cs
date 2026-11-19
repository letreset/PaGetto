using PaGetto.Core.CocoaPods;
using PaGetto.Core.Entities;
using PaGetto.Core.Go;
using PaGetto.Core.PyPI;

namespace PaGetto.Core.Artifacts;

/// <summary>
/// Maps the name of a package as a user or a link gives it to the name it is stored under.
/// </summary>
public static class ArtifactNames
{
    /// <summary>
    /// The name a package is stored under: lowercased, and for Python projects normalized as well,
    /// so <c>My_Package</c> is <c>my-package</c>. A Go module path is case-sensitive, so it is
    /// escaped instead: <c>example.com/My</c> is <c>example.com/!my</c>, and an escaped path, which
    /// the links of the web UI use, is taken as it is. Pod names are case-sensitive too, and escaped
    /// the same way.
    /// </summary>
    public static string GetStoredName(FeedType feedType, string name)
    {
        return feedType switch
        {
            FeedType.PyPI => PyPIPackageName.Normalize(name),
            FeedType.Go => GoModulePath.TryUnescape(name, out _) ? name : GoModulePath.Escape(name),
            FeedType.CocoaPods => CocoaPodsPodName.TryUnescape(name, out _) ? name : CocoaPodsPodName.Escape(name),
            _ => name.ToLowerInvariant(),
        };
    }
}
