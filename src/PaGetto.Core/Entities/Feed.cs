using System;
using System.Collections.Generic;
using PaGetto.Core.Configuration;

namespace PaGetto.Core.Entities;

public class Feed
{
    public const string DefaultSlug = "default";
    public static readonly Guid DefaultId = new("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; }
    public string Slug { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public int SortOrder { get; set; }

    // Per-feed overrides — null means "use global default"
    public PackageOverwriteAllowed? AllowPackageOverwrites { get; set; }
    public PackageDeletionBehavior? PackageDeletionBehavior { get; set; }
    public bool? IsReadOnlyMode { get; set; }
    public uint? MaxPackageSizeMiB { get; set; }
    public int? RetentionMaxMajorVersions { get; set; }
    public int? RetentionMaxMinorVersions { get; set; }
    public int? RetentionMaxPatchVersions { get; set; }
    public int? RetentionMaxPrereleaseVersions { get; set; }
    public bool? RetentionDeletePrereleasesOfOlderMajors { get; set; }
    public int? UpstreamListingCacheSeconds { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public List<Package> Packages { get; set; }
    public List<FeedPermission> Permissions { get; set; }
    public List<FeedMirror> Mirrors { get; set; } = [];
}