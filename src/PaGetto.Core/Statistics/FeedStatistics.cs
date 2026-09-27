using System.Collections.Generic;

namespace PaGetto.Core.Statistics;

/// <summary>
/// Download, size and version statistics of one feed, for the statistics page.
/// </summary>
public class FeedStatistics
{
    public long TotalDownloads { get; init; }

    /// <summary>
    /// The size of all stored package files. Versions without a recorded size aren't counted.
    /// </summary>
    public long TotalSizeBytes { get; init; }

    public int StableVersions { get; init; }
    public int PrereleaseVersions { get; init; }
    public int UnlistedVersions { get; init; }

    /// <summary>
    /// The packages with the most downloads over all their versions, most downloaded first.
    /// Packages without downloads aren't listed.
    /// </summary>
    public IReadOnlyList<PackageDownloadCount> MostDownloaded { get; init; } = [];

    /// <summary>
    /// The most recently published listed versions, newest first.
    /// </summary>
    public IReadOnlyList<PublishedPackageVersion> RecentlyPublished { get; init; } = [];
}
