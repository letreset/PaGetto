using System;
using System.Collections.Generic;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Feeds;

public interface IFeedSettingsResolver
{
    PackageOverwriteAllowed GetAllowPackageOverwrites(Feed feed);
    PackageDeletionBehavior GetPackageDeletionBehavior(Feed feed);
    bool GetIsReadOnlyMode(Feed feed);
    uint GetMaxPackageSizeMiB(Feed feed);
    RetentionOptions GetRetentionOptions(Feed feed);

    /// <summary>
    /// The version limits of a feed that is not a NuGet feed: only the feed's own settings. The
    /// global <see cref="PaGettoOptions.Retention"/> is for NuGet feeds, as it was before other
    /// feed types had retention.
    /// </summary>
    RetentionOptions GetArtifactRetentionOptions(Feed feed);

    /// <summary>
    /// How long upstream package listings are cached for the feed. Zero when the cache is off.
    /// </summary>
    TimeSpan GetUpstreamListingCacheDuration(Feed feed);

    /// <summary>
    /// After how many days without a download a mirrored version of the feed is removed. Zero
    /// when they are kept.
    /// </summary>
    int GetMirrorCacheRetentionDays(Feed feed);

    /// <summary>
    /// The feed's enabled mirrors, in priority order. Empty when the feed does not mirror.
    /// </summary>
    IReadOnlyList<MirrorOptions> GetMirrorOptions(Feed feed);
}
