using System;
using System.Collections.Generic;
using System.Linq;
using PaGetto.Core.Entities;
using PaGetto.Core.Pub;

namespace PaGetto.Core.Artifacts;

/// <summary>
/// Orders the versions of an <see cref="Artifact"/>. Each ecosystem has its own version rules, so
/// the order depends on the type of the feed.
/// </summary>
public static class ArtifactVersionOrder
{
    /// <summary>
    /// The comparer for the version strings of a feed type, lowest version first.
    /// </summary>
    public static IComparer<string> GetComparer(FeedType feedType)
    {
        return feedType switch
        {
            FeedType.Pub => PubVersion.StringComparer,
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
    /// The version shown for a package by default: the newest listed stable version, or the newest
    /// listed one when there is no stable version, or the newest one when none is listed.
    /// </summary>
    /// <returns>The version, or null if there are no versions.</returns>
    public static ArtifactVersion GetLatest(FeedType feedType, IEnumerable<ArtifactVersion> versions)
    {
        var sorted = SortDescending(feedType, versions);

        return sorted.FirstOrDefault(v => v.Listed && !v.IsPrerelease)
            ?? sorted.FirstOrDefault(v => v.Listed)
            ?? sorted.FirstOrDefault();
    }
}
