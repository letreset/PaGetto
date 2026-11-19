namespace PaGetto.Web.Controllers;

/// <summary>
/// The body of <c>POST {feed}/api/v1/promote</c>.
/// </summary>
public class PromotionRequest
{
    /// <summary>
    /// The package as the feed stores it (the id of a NuGet package).
    /// </summary>
    public string PackageId { get; set; }

    public string Version { get; set; }

    /// <summary>
    /// The slug of the feed to promote the version to.
    /// </summary>
    public string TargetFeed { get; set; }

    /// <summary>
    /// Whether the version is removed from the current feed afterwards.
    /// </summary>
    public bool Move { get; set; }
}
