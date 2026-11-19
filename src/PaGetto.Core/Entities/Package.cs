using System;
using System.Collections.Generic;
using System.Diagnostics;
using NuGet.Versioning;

namespace PaGetto.Core.Entities;

[DebuggerDisplay("{Id} {Version}")]
public class Package
{
    public int Key { get; set; }

    public Guid FeedId { get; set; }
    public Feed Feed { get; set; }

    public string Id { get; set; }

    public NuGetVersion Version
    {
        get
        {
            // Favor the original version string as it contains more information.
            // Packages uploaded with older versions of PaGetto may not have the original version string.
            return NuGetVersion.Parse(
                OriginalVersionString ?? NormalizedVersionString);
        }

        set
        {
            NormalizedVersionString = value.ToNormalizedString().ToLowerInvariant();
            OriginalVersionString = value.OriginalVersion;
        }
    }

    public string[] Authors { get; set; }
    public string Description { get; set; }
    public long Downloads { get; set; }

    /// <summary>
    /// When the package was last downloaded, or null if it hasn't been since this was recorded.
    /// A package stored from a mirror that isn't downloaded for the feed's
    /// <see cref="Feed.MirrorCacheRetentionDays"/> is removed.
    /// </summary>
    public DateTime? LastDownloadedAtUtc { get; set; }

    /// <summary>
    /// When the version was last matched against the vulnerability database, or null if it hasn't
    /// been yet. Versions that were never scanned, or not for a while, are scanned in the background.
    /// </summary>
    public DateTime? VulnerabilitiesScannedAtUtc { get; set; }

    /// <summary>
    /// When the version was quarantined, or null if it is not. A quarantined version is stored but
    /// neither served nor listed (see <see cref="Quarantine.QuarantineService"/>).
    /// </summary>
    public DateTime? QuarantinedAtUtc { get; set; }

    /// <summary>
    /// Why the version is quarantined, e.g. the advisory of the policy it fails.
    /// </summary>
    public string QuarantineReason { get; set; }

    /// <summary>
    /// When an administrator released the version from quarantine. The feed's policies are no
    /// longer applied to a released version.
    /// </summary>
    public DateTime? QuarantineReleasedAtUtc { get; set; }
    public bool HasReadme { get; set; }
    public bool HasEmbeddedIcon { get; set; }
    public bool IsPrerelease { get; set; }
    public string CachedFrom { get; set; }
    public string ReleaseNotes { get; set; }
    public string Language { get; set; }
    public bool Listed { get; set; }
    public string MinClientVersion { get; set; }
    public DateTime Published { get; set; }
    public bool RequireLicenseAcceptance { get; set; }
    public SemVerLevel SemVerLevel { get; set; }
    public string Summary { get; set; }
    public string Title { get; set; }

    public string Copyright { get; set; }

    /// <summary>
    /// The SPDX license expression (e.g. "MIT") when the nuspec declares one. Null for license URLs
    /// and license files.
    /// </summary>
    public string LicenseExpression { get; set; }

    /// <summary>
    /// The length of the .nupkg in bytes. Null for versions that are only available from a mirror,
    /// and for packages stored before this was recorded until the backfill reaches them.
    /// </summary>
    public long? Size { get; set; }

    public Uri IconUrl { get; set; }
    public Uri LicenseUrl { get; set; }
    public Uri ProjectUrl { get; set; }

    public Uri RepositoryUrl { get; set; }
    public string RepositoryType { get; set; }

    public string[] Tags { get; set; }

    /// <summary>
    /// Used for optimistic concurrency.
    /// </summary>
    public byte[] RowVersion { get; set; }

    public List<PackageDependency> Dependencies { get; set; }
    public List<PackageType> PackageTypes { get; set; }
    public List<TargetFramework> TargetFrameworks { get; set; }

    public string NormalizedVersionString { get; set; }
    public string OriginalVersionString { get; set; }


    public string IconUrlString => IconUrl?.AbsoluteUri ?? string.Empty;
    public string LicenseUrlString => LicenseUrl?.AbsoluteUri ?? string.Empty;
    public string ProjectUrlString => ProjectUrl?.AbsoluteUri ?? string.Empty;
    public string RepositoryUrlString => RepositoryUrl?.AbsoluteUri ?? string.Empty;
}
