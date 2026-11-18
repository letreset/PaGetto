using System;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Feeds;

/// <summary>
/// Rules built from a feed's settings that every package type applies the same way.
/// </summary>
public static class FeedSettingsResolverExtensions
{
    private const long BytesPerMiB = 1024L * 1024;

    /// <summary>
    /// Whether a version that the feed already has may be published again with other content.
    /// </summary>
    /// <param name="feedSettings">The settings.</param>
    /// <param name="feed">The feed.</param>
    /// <param name="isPrerelease">Whether the version is a prerelease.</param>
    public static bool CanOverwrite(this IFeedSettingsResolver feedSettings, Feed feed, bool isPrerelease)
    {
        ArgumentNullException.ThrowIfNull(feedSettings);

        return feedSettings.GetAllowPackageOverwrites(feed) switch
        {
            PackageOverwriteAllowed.True => true,
            PackageOverwriteAllowed.PrereleaseOnly => isPrerelease,
            _ => false,
        };
    }

    /// <summary>
    /// The largest package file the feed takes, in bytes.
    /// </summary>
    public static long GetMaxPackageSizeBytes(this IFeedSettingsResolver feedSettings, Feed feed)
    {
        ArgumentNullException.ThrowIfNull(feedSettings);

        return feedSettings.GetMaxPackageSizeMiB(feed) * BytesPerMiB;
    }
}
