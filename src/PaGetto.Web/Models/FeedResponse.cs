using System;
using System.Collections.Generic;
using System.Linq;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;

namespace PaGetto.Web.Models;

/// <summary>
/// Safe projection of a <see cref="Feed"/> for API responses.
/// Omits secret fields (password, token, custom headers) and replaces
/// them with boolean presence indicators.
/// </summary>
public class FeedResponse
{
    public Guid Id { get; set; }
    public string Slug { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public int SortOrder { get; set; }

    public PackageOverwriteAllowed? AllowPackageOverwrites { get; set; }
    public PackageDeletionBehavior? PackageDeletionBehavior { get; set; }
    public bool? IsReadOnlyMode { get; set; }
    public uint? MaxPackageSizeGiB { get; set; }
    public int? RetentionMaxMajorVersions { get; set; }
    public int? RetentionMaxMinorVersions { get; set; }
    public int? RetentionMaxPatchVersions { get; set; }
    public int? RetentionMaxPrereleaseVersions { get; set; }
    public int? UpstreamListingCacheSeconds { get; set; }

    /// <summary>The feed's upstream mirrors, in priority order.</summary>
    public List<FeedMirrorResponse> Mirrors { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public static FeedResponse FromFeed(Feed feed) => new FeedResponse
    {
        Id = feed.Id,
        Slug = feed.Slug,
        Name = feed.Name,
        Description = feed.Description,
        SortOrder = feed.SortOrder,
        AllowPackageOverwrites = feed.AllowPackageOverwrites,
        PackageDeletionBehavior = feed.PackageDeletionBehavior,
        IsReadOnlyMode = feed.IsReadOnlyMode,
        MaxPackageSizeGiB = feed.MaxPackageSizeGiB,
        RetentionMaxMajorVersions = feed.RetentionMaxMajorVersions,
        RetentionMaxMinorVersions = feed.RetentionMaxMinorVersions,
        RetentionMaxPatchVersions = feed.RetentionMaxPatchVersions,
        RetentionMaxPrereleaseVersions = feed.RetentionMaxPrereleaseVersions,
        UpstreamListingCacheSeconds = feed.UpstreamListingCacheSeconds,
        Mirrors = (feed.Mirrors ?? [])
            .OrderBy(m => m.SortOrder)
            .Select(FeedMirrorResponse.FromMirror)
            .ToList(),
        CreatedAtUtc = feed.CreatedAtUtc,
        UpdatedAtUtc = feed.UpdatedAtUtc,
    };
}
