using System;
using System.Collections.Generic;
using System.Linq;
using PaGetto.Core.Entities;
using PaGetto.Core.Maven;
using PaGetto.Core.Npm;
using PaGetto.Core.Pub;

namespace PaGetto.Core.Artifacts;

/// <summary>
/// Orders the versions of an <see cref="Artifact"/>. Each ecosystem has its own version rules, so
/// the order depends on the type of the feed.
/// </summary>
public static class ArtifactVersionOrder
{
    /// <summary>
    /// The tag that names the version clients install by default.
    /// </summary>
    public const string LatestTag = "latest";

    /// <summary>
    /// The comparer for the version strings of a feed type, lowest version first.
    /// </summary>
    public static IComparer<string> GetComparer(FeedType feedType)
    {
        return feedType switch
        {
            FeedType.Pub => PubVersion.StringComparer,
            FeedType.Npm => NpmVersion.StringComparer,
            FeedType.Maven => MavenVersion.StringComparer,
            _ => throw new NotSupportedException($"Feeds of type {feedType} do not store artifacts."),
        };
    }

    /// <summary>
    /// Sorts versions, newest first.
    /// </summary>
    public static List<ArtifactVersion> SortDescending(FeedType feedType, IEnumerable<ArtifactVersion> versions)
    {
        return versions.OrderByDescending(v => v.Version, GetComparer(feedType)).ToList();
    }

    /// <summary>
    /// The version shown for a package by default: the one its <c>latest</c> tag points at (an npm
    /// dist-tag, which clients install by default), or else the newest listed stable version, or
    /// the newest listed one when there is no stable version, or the newest one when none is listed.
    /// </summary>
    /// <param name="feedType">The type of the package's feed.</param>
    /// <param name="versions">The package's versions.</param>
    /// <param name="tags">The package's tags, or null when it has none.</param>
    /// <returns>The version, or null if there are no versions.</returns>
    public static ArtifactVersion GetLatest(FeedType feedType, IEnumerable<ArtifactVersion> versions, IEnumerable<ArtifactTag> tags = null)
    {
        var sorted = SortDescending(feedType, versions);

        var latestTag = tags?.FirstOrDefault(t => t.Name == LatestTag);
        var tagged = latestTag == null
            ? null
            : sorted.FirstOrDefault(v => string.Equals(v.Version, latestTag.Version, StringComparison.OrdinalIgnoreCase));

        return tagged
            ?? sorted.FirstOrDefault(v => v.Listed && !v.IsPrerelease)
            ?? sorted.FirstOrDefault(v => v.Listed)
            ?? sorted.FirstOrDefault();
    }
}
