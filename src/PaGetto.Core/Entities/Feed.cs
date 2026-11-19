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

    /// <summary>
    /// The package ecosystem the feed serves. Set when the feed is created and never changed.
    /// </summary>
    public FeedType Type { get; set; }

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

    /// <summary>
    /// After how many days without a download a version stored from a mirror is removed; it is
    /// mirrored again when it is next requested. 0 keeps them; null uses
    /// <see cref="Configuration.PaGettoOptions.MirrorCacheRetentionDays"/>.
    /// </summary>
    public int? MirrorCacheRetentionDays { get; set; }

    /// <summary>
    /// Whether versions that the malicious packages data lists are refused from the feed's mirrors;
    /// null uses <see cref="Configuration.MaliciousPackageOptions.Enabled"/>.
    /// </summary>
    public bool? BlockMaliciousPackages { get; set; }

    /// <summary>
    /// Versions with a known vulnerability of this severity or higher are quarantined, and new
    /// versions wait in quarantine for their first scan; null for no such policy.
    /// </summary>
    public VulnerabilitySeverity? QuarantineSeverity { get; set; }

    /// <summary>
    /// Whether versions that the malicious packages data lists are quarantined, including versions
    /// published to the feed.
    /// </summary>
    public bool QuarantineMaliciousPackages { get; set; }

    /// <summary>
    /// The URL that is sent a POST for every package published, unlisted, relisted or deleted in
    /// the feed, or null for none.
    /// </summary>
    public string WebhookUrl { get; set; }

    /// <summary>
    /// The secret the webhook's body is signed with (HMAC-SHA256, in <c>X-PaGetto-Signature</c>), or null.
    /// </summary>
    public string WebhookSecret { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Goes up by one whenever a package of a feed that is not a NuGet feed changes. Indexes built
    /// from all of the feed's packages are cached under it (see
    /// <see cref="Artifacts.ArtifactIndexCache"/>), so a change makes the next request build them again.
    /// </summary>
    public long ArtifactRevision { get; set; }

    public List<Package> Packages { get; set; }
    public List<FeedPermission> Permissions { get; set; }
    public List<FeedMirror> Mirrors { get; set; } = [];
}