using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Vulnerabilities;

namespace PaGetto.Core.MaliciousPackages;

/// <summary>
/// Refuses the versions that the malicious packages data lists, before they are mirrored and
/// before a mirrored copy is served. Versions published to the feed itself are never refused: a
/// private package often has the name of a malicious public one, since dependency confusion
/// attacks publish public packages under private names. An administrator's
/// <see cref="MaliciousPackageAllowance"/> and the feed's
/// <see cref="Feed.BlockMaliciousPackages"/> setting let a listed version through.
/// </summary>
public partial class MaliciousPackageGuard
{
    private static readonly IComparer<string> NuGetComparer = Comparer<string>.Create(
        (a, b) => NuGetVersion.Parse(a).CompareTo(NuGetVersion.Parse(b)));

    // OSV has Go versions without the "v" that the go command writes.
    private static readonly IComparer<string> GoComparer = Comparer<string>.Create(
        (a, b) => Go.GoVersion.StringComparer.Compare(AddV(a), AddV(b)));

    private readonly IContext _context;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly ILogger<MaliciousPackageGuard> _logger;

    public MaliciousPackageGuard(IContext context, IFeedSettingsResolver feedSettings, ILogger<MaliciousPackageGuard> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Throws when a version of an artifact feed must not be mirrored or served.
    /// </summary>
    /// <param name="feed">The feed, with its mirrors.</param>
    /// <param name="name">The name the package is stored under.</param>
    /// <param name="version">The version.</param>
    /// <param name="stored">The stored version, or null when it would be mirrored.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <exception cref="MaliciousPackageBlockedException">The version is listed and not allowed.</exception>
    public Task EnsureAllowedAsync(Feed feed, string name, string version, ArtifactVersion stored, CancellationToken cancellationToken)
    {
        return EnsureAllowedAsync(feed, name, version, stored != null, stored?.CachedFrom, cancellationToken);
    }

    /// <summary>
    /// Throws when a version of a NuGet package must not be mirrored or served.
    /// </summary>
    /// <exception cref="MaliciousPackageBlockedException">The version is listed and not allowed.</exception>
    public Task EnsureAllowedAsync(Feed feed, string id, NuGetVersion version, Package stored, CancellationToken cancellationToken)
    {
        return EnsureAllowedAsync(feed, id, version?.ToNormalizedString(), stored != null, stored?.CachedFrom, cancellationToken);
    }

    /// <summary>
    /// Throws when a version must not be mirrored or served.
    /// </summary>
    /// <param name="feed">The feed, with its mirrors.</param>
    /// <param name="name">The name the package is stored under.</param>
    /// <param name="version">The version.</param>
    /// <param name="isStored">Whether the feed has the version; otherwise it would be mirrored.</param>
    /// <param name="cachedFrom">The mirror a stored version came from, or null for a published one.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <exception cref="MaliciousPackageBlockedException">The version is listed and not allowed.</exception>
    public async Task EnsureAllowedAsync(Feed feed, string name, string version, bool isStored, string cachedFrom, CancellationToken cancellationToken)
    {
        if (feed == null || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(version) || (isStored && cachedFrom == null))
        {
            return;
        }

        var listing = await FindBlockingListingOrNullAsync(feed, name, version, cancellationToken);

        // A version the feed doesn't have and can't mirror is answered as missing.
        if (listing == null || (!isStored && _feedSettings.GetMirrorOptions(feed).Count == 0))
        {
            return;
        }

        var displayName = MaliciousPackageName.GetOsvName(feed.Type, name) ?? name;
        LogBlocked(listing.AdvisoryId, displayName, version, feed.Slug);

        throw new MaliciousPackageBlockedException(feed.Slug, displayName, version, listing.AdvisoryId, listing.Summary);
    }

    /// <summary>
    /// Finds the listing that blocks a version on a feed: the version is listed, not allowed, and
    /// the feed blocks malicious packages.
    /// </summary>
    /// <returns>The listing, or null when the version is not blocked.</returns>
    public async Task<MaliciousPackage> FindBlockingListingOrNullAsync(Feed feed, string name, string version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feed);
        if (!_feedSettings.GetBlockMaliciousPackages(feed))
        {
            return null;
        }

        return await FindListingOrNullAsync(feed.Type, name, version, cancellationToken);
    }

    /// <summary>
    /// Finds the listing of a version that is listed and not allowed, whatever the feed's settings.
    /// </summary>
    /// <returns>The listing, or null when the version is not listed or is allowed.</returns>
    public async Task<MaliciousPackage> FindListingOrNullAsync(FeedType feedType, string name, string version, CancellationToken cancellationToken)
    {
        var ecosystem = OsvEcosystem.GetName(feedType);
        var osvName = ecosystem == null ? null : MaliciousPackageName.GetOsvName(feedType, name);
        if (osvName == null)
        {
            return null;
        }

        var normalized = MaliciousPackageName.Normalize(ecosystem, osvName);
        var listings = await _context.MaliciousPackages
            .AsNoTracking()
            .Where(p => p.Ecosystem == ecosystem && p.Name == normalized)
            .ToListAsync(cancellationToken);
        if (listings.Count == 0)
        {
            return null;
        }

        var comparer = GetComparer(feedType);
        var listing = listings
            .OrderBy(p => p.AdvisoryId, StringComparer.Ordinal)
            .FirstOrDefault(p => MaliciousVersions.IsAffected(p.AffectedVersions, version, comparer));
        if (listing == null)
        {
            return null;
        }

        var allowances = await _context.MaliciousPackageAllowances
            .AsNoTracking()
            .Where(a => a.Ecosystem == ecosystem && a.Name == normalized)
            .ToListAsync(cancellationToken);

        return allowances.Any(a => a.Version == null || MaliciousVersions.AreSame(a.Version, version, comparer))
            ? null
            : listing;
    }

    /// <summary>
    /// The comparer of the versions of a feed type, as the malicious packages data writes them.
    /// </summary>
    public static IComparer<string> GetComparer(FeedType feedType)
    {
        return feedType switch
        {
            FeedType.NuGet => NuGetComparer,
            FeedType.Go => GoComparer,
            _ => ArtifactVersionOrder.GetComparer(feedType),
        };
    }

    private static string AddV(string version)
    {
        return version.StartsWith('v') ? version : "v" + version;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blocked {PackageId} {PackageVersion} on feed {Feed}: it is listed as a malicious package ({AdvisoryId}).")]
    private partial void LogBlocked(string advisoryId, string packageId, string packageVersion, string feed);
}
