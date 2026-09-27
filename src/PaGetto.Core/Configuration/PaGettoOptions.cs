using System;

namespace PaGetto.Core.Configuration;

public class PaGettoOptions
{
    /// <summary>
    /// The API Key required to authenticate package
    /// operations. If <see cref="ApiKey"/> is not set, package operations do not require authentication.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// The application root URL for usage in reverse proxy scenarios.
    /// </summary>
    public string PathBase { get; set; }

    /// <summary>
    /// If enabled, the database will be updated at app startup by running
    /// Entity Framework migrations. This is not recommended in production.
    /// </summary>
    public bool RunMigrationsAtStartup { get; set; } = true;

    /// <summary>
    /// How PaGetto should interpret package deletion requests.
    /// </summary>
    public PackageDeletionBehavior PackageDeletionBehavior { get; set; } = PackageDeletionBehavior.Unlist;

    /// <summary>
    /// If enabled, pushing a package that already exists will replace the
    /// existing package.
    /// </summary>
    public PackageOverwriteAllowed AllowPackageOverwrites { get; set; } = PackageOverwriteAllowed.False;

    /// <summary>
    /// If true, disables package pushing, deleting, and re-listing.
    /// </summary>
    public bool IsReadOnlyMode { get; set; } = false;

    /// <summary>
    /// The URLs the PaGetto server will use.
    /// As per documentation <a href="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host?view=aspnetcore-3.1#server-urls">here (Server URLs)</a>.
    /// </summary>
    public string Urls { get; set; }

    public const uint DefaultMaxPackageSizeMiB = 8192;

    /// <summary>
    /// The maximum package size in MiB, the default for feeds without their own limit.
    /// It also sets the server's request body limit. Leave it unset for the default of 8192 MiB (8 GiB).
    /// </summary>
    public uint? MaxPackageSizeMiB { get; set; }

    /// <summary>
    /// The old maximum package size setting in GiB. Only read when <see cref="MaxPackageSizeMiB"/> is not set.
    /// </summary>
    [Obsolete("Use MaxPackageSizeMiB.")]
    public uint? MaxPackageSizeGiB { get; set; }

    /// <summary>
    /// The configured maximum package size in MiB: <see cref="MaxPackageSizeMiB"/>, else the legacy
    /// <c>MaxPackageSizeGiB</c> converted to MiB, else <see cref="DefaultMaxPackageSizeMiB"/>.
    /// </summary>
#pragma warning disable CS0618 // The legacy GiB setting is still honored.
    public uint EffectiveMaxPackageSizeMiB => MaxPackageSizeMiB ?? MaxPackageSizeGiB * 1024 ?? DefaultMaxPackageSizeMiB;
#pragma warning restore CS0618

    /// <summary>
    /// The maximum number of package versions in a single registration page.
    /// Packages with more versions than this return a paged registration index,
    /// and clients fetch each page separately.
    /// </summary>
    public int RegistrationPageSize { get; set; } = 64;

    /// <summary>
    /// How long, in seconds, a mirrored feed keeps an upstream package listing (versions and
    /// metadata) in memory before asking the upstream again. 0 disables the cache.
    /// This is the default for feeds that don't override it in their settings.
    /// </summary>
    public int UpstreamListingCacheSeconds { get; set; } = 300;

    /// <summary>
    /// If this is set to a value, it will limit the number of versions that can be pushed for a package.
    /// the older versions will be deleted.
    /// This setting is not used anymore and is deprecated.
    /// </summary>
    [Obsolete("MaxVersionsPerPackage is deprecated. Please configure RetentionOptions parameters instead.")]
    public uint? MaxVersionsPerPackage { get; set; } = null;

    public RetentionOptions Retention { get; set; }

    public DatabaseOptions Database { get; set; }

    public StorageOptions Storage { get; set; }

    public SearchOptions Search { get; set; }

    /// <summary>
    /// Global mirror configuration. Kept for backward compatibility: on first startup after
    /// upgrading to multi-feed support, these settings are copied to the default feed and are
    /// no longer read at runtime. Configure mirroring per-feed via the admin UI instead.
    /// </summary>
    [Obsolete("Mirror config is now per-feed. This property is only read once at upgrade time to seed the default feed; configure mirroring via the admin UI or FeedSettings.")]
    public MirrorOptions Mirror { get; set; }

    public HealthCheckOptions HealthCheck { get; set; } = new();

    public StatisticsOptions Statistics { get; set; } = new();

    public CorsPolicyOptions Cors { get; set; } = new();

    public SecurityHeadersOptions SecurityHeaders { get; set; } = new();

    public RequestRateLimitOptions RequestRateLimit { get; set; } = new();

    public NugetAuthenticationOptions Authentication { get; set; }

    public EmailOptions Email { get; set; }

    public PatExpiryNotificationOptions PatExpiryNotification { get; set; }
}
