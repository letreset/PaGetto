using PaGetto.Core.Entities;

namespace PaGetto.Web.Helper;

/// <summary>
/// What the web UI shows for each feed type.
/// </summary>
public static class FeedTypeNames
{
    /// <summary>
    /// The name of the ecosystem, e.g. "NuGet", "Dart", "npm", "Maven", "Python", "Rust", "Go", "Ruby", "Hex", "PHP", "CocoaPods", "Swift", "R", "Ivy", "VSIX" or "VS Code".
    /// </summary>
    public static string GetDisplayName(FeedType type)
    {
        return type switch
        {
            FeedType.Pub => "Dart",
            FeedType.Npm => "npm",
            FeedType.Maven => "Maven",
            FeedType.PyPI => "Python",
            FeedType.Cargo => "Rust",
            FeedType.Go => "Go",
            FeedType.RubyGems => "Ruby",
            FeedType.Hex => "Hex",
            FeedType.Composer => "PHP",
            FeedType.CocoaPods => "CocoaPods",
            FeedType.Swift => "Swift",
            FeedType.Cran => "R",
            FeedType.Ivy => "Ivy",
            FeedType.Vsix => "VSIX",
            FeedType.VsCode => "VS Code",
            _ => "NuGet",
        };
    }

    /// <summary>
    /// The URL clients are configured with: the service index of a NuGet feed, the hosted URL of a
    /// Dart feed, the registry URL of an npm feed, the repository URL of a Maven feed, the index
    /// URL of a PyPI feed, the sparse index URL of a Cargo feed, the GOPROXY URL of a Go feed, the
    /// gem source URL of a RubyGems feed, the repository URL of a Hex feed, the repository URL of a
    /// Composer feed, the spec repository URL of a CocoaPods feed, the registry URL of a Swift feed, the
    /// repository URL of a CRAN feed, the repository URL of an Ivy feed, the gallery feed URL of a VSIX feed, or the
    /// gallery service URL of a VS Code feed.
    /// </summary>
    /// <param name="type">The feed's type.</param>
    /// <param name="feedUrl">The feed's root URL, e.g. <c>https://example.com/feeds/internal</c>.</param>
    public static string GetClientUrl(FeedType type, string feedUrl)
    {
        feedUrl = feedUrl.TrimEnd('/');

        return type switch
        {
            FeedType.Pub => feedUrl,
            FeedType.Npm => feedUrl + "/npm/",
            FeedType.Maven => feedUrl + "/maven2/",
            FeedType.PyPI => feedUrl + "/pypi/simple/",
            FeedType.Cargo => "sparse+" + feedUrl + "/cargo/index/",
            FeedType.Go => feedUrl + "/go",
            FeedType.RubyGems => feedUrl + "/rubygems/",
            FeedType.Hex => feedUrl + "/hex",
            FeedType.Composer => feedUrl + "/composer",
            FeedType.CocoaPods => feedUrl + "/cocoapods/",
            FeedType.Swift => feedUrl + "/swift",
            FeedType.Cran => feedUrl + "/cran",
            FeedType.Ivy => feedUrl + "/ivy",
            FeedType.Vsix => feedUrl + "/vsix/atom.xml",
            FeedType.VsCode => feedUrl + "/vscode/gallery",
            _ => feedUrl + "/v3/index.json",
        };
    }

    /// <summary>
    /// The Razor page that shows a package of a feed of this type. Both pages take the route values
    /// <c>id</c> and <c>version</c>.
    /// </summary>
    public static string GetPackagePage(FeedType type)
    {
        return type == FeedType.NuGet ? "/Package" : "/Artifact";
    }
}
