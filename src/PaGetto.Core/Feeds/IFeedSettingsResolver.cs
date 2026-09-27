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
    /// How long upstream package listings are cached for the feed. Zero when the cache is off.
    /// </summary>
    TimeSpan GetUpstreamListingCacheDuration(Feed feed);

    /// <summary>
    /// The feed's enabled mirrors, in priority order. Empty when the feed does not mirror.
    /// </summary>
    IReadOnlyList<MirrorOptions> GetMirrorOptions(Feed feed);
}
