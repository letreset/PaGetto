using PaGetto.Core.Entities;

namespace PaGetto.Web.Helper;

/// <summary>
/// What the web UI shows for each feed type.
/// </summary>
public static class FeedTypeNames
{
    /// <summary>
    /// The name of the ecosystem, e.g. "NuGet", "Dart" or "npm".
    /// </summary>
    public static string GetDisplayName(FeedType type)
    {
        return type switch
        {
            FeedType.Pub => "Dart",
            FeedType.Npm => "npm",
            _ => "NuGet",
        };
    }

    /// <summary>
    /// The URL clients are configured with: the service index of a NuGet feed, the hosted URL of a
    /// Dart feed, or the registry URL of an npm feed.
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
