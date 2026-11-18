using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Indexing;
using PaGetto.Core.Diagnostics;

namespace PaGetto.Core.Retention;

/// <summary>
/// Removes what the feeds' retention settings no longer keep: versions stored from a mirror that
/// haven't been downloaded for the feed's <see cref="Feed.MirrorCacheRetentionDays"/> (they are
/// mirrored again when they are next requested), and the versions of packages of feeds that are
/// not NuGet feeds beyond the feed's own version limits (NuGet feeds apply theirs on push).
/// </summary>
public partial class RetentionCleaner
{
    private readonly IContext _context;
    private readonly IFeedService _feeds;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly IPackageDeletionService _packageDeletion;
    private readonly SystemTime _time;
    private readonly ILogger<RetentionCleaner> _logger;

    public RetentionCleaner(
        IContext context,
        IFeedService feeds,
        IFeedSettingsResolver feedSettings,
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        IPackageDeletionService packageDeletion,
        SystemTime time,
        ILogger<RetentionCleaner> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _feeds = feeds ?? throw new ArgumentNullException(nameof(feeds));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _packageDeletion = packageDeletion ?? throw new ArgumentNullException(nameof(packageDeletion));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Cleans every feed.
    /// </summary>
    /// <returns>The number of removed versions.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var removed = 0;
        foreach (var feed in await _feeds.GetAllFeedsAsync(cancellationToken))
        {
            var days = _feedSettings.GetMirrorCacheRetentionDays(feed);
            if (days > 0)
            {
                var unused = feed.Type == FeedType.NuGet
                    ? await RemoveUnusedMirroredPackagesAsync(feed, days, cancellationToken)
                    : await RemoveUnusedMirroredVersionsAsync(feed, days, cancellationToken);

                PaGettoMetrics.RecordPackagesRemoved(unused, unusedMirror: true);
                removed += unused;
            }

            if (feed.Type != FeedType.NuGet)
            {
                var beyondLimits = await ApplyVersionLimitsAsync(feed, cancellationToken);

                PaGettoMetrics.RecordPackagesRemoved(beyondLimits, unusedMirror: false);
                removed += beyondLimits;
            }
        }

        return removed;
    }

    private async Task<int> RemoveUnusedMirroredVersionsAsync(Feed feed, int days, CancellationToken cancellationToken)
    {
        var now = _time.UtcNow;
        var cutoff = now.AddDays(-days);

        // Versions stored before downloads were timed count from their first cleanup.
        await _context.ArtifactVersions
            .Where(v => v.Artifact.FeedId == feed.Id && v.CachedFrom != null && v.LastDownloadedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.LastDownloadedAtUtc, now), cancellationToken);

        var unused = await _context.ArtifactVersions
            .AsNoTracking()
            .Where(v => v.Artifact.FeedId == feed.Id && v.CachedFrom != null && v.LastDownloadedAtUtc < cutoff)
            .Select(v => new { v.Artifact.Name, v.Version })
            .ToListAsync(cancellationToken);

        var removed = 0;
        foreach (var version in unused)
        {
            if (await ArtifactPublisher.RemoveAsync(_artifacts, _storage, feed, version.Name, version.Version, cancellationToken))
            {
                LogUnusedMirroredVersionRemoved(feed.Slug, version.Name, version.Version, days);
                removed++;
            }
        }

        return removed;
    }

    private async Task<int> RemoveUnusedMirroredPackagesAsync(Feed feed, int days, CancellationToken cancellationToken)
    {
        var now = _time.UtcNow;
        var cutoff = now.AddDays(-days);

        // Packages stored before downloads were timed count from their first cleanup.
        await _context.Packages
            .Where(p => p.FeedId == feed.Id && p.CachedFrom != null && p.LastDownloadedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastDownloadedAtUtc, now), cancellationToken);

        var unused = await _context.Packages
            .AsNoTracking()
            .Where(p => p.FeedId == feed.Id && p.CachedFrom != null && p.LastDownloadedAtUtc < cutoff)
            .Select(p => new { p.Id, p.NormalizedVersionString })
            .ToListAsync(cancellationToken);

        var removed = 0;
        foreach (var package in unused)
        {
            var version = NuGetVersion.Parse(package.NormalizedVersionString);
            if (await _packageDeletion.TryHardDeletePackageAsync(feed.Id, feed.Slug, package.Id, version, cancellationToken))
            {
                LogUnusedMirroredVersionRemoved(feed.Slug, package.Id, package.NormalizedVersionString, days);
                removed++;
            }
        }

        return removed;
    }

    private async Task<int> ApplyVersionLimitsAsync(Feed feed, CancellationToken cancellationToken)
    {
        var options = _feedSettings.GetArtifactRetentionOptions(feed);
        if (!ArtifactRetention.HasLimits(options))
        {
            return 0;
        }

        var versions = await _artifacts.FindAllVersionsAsync(feed.Id, cancellationToken);

        var removed = 0;
        foreach (var package in versions.GroupBy(v => v.Artifact.Name))
        {
            foreach (var version in ArtifactRetention.GetVersionsToRemove(feed.Type, package.ToList(), options))
            {
                if (await ArtifactPublisher.RemoveAsync(_artifacts, _storage, feed, package.Key, version.Version, cancellationToken))
                {
                    LogVersionBeyondLimitsRemoved(feed.Slug, package.Key, version.Version);
                    removed++;
                }
            }
        }

        return removed;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {PackageId} {PackageVersion} of feed {Feed}, stored from a mirror and not downloaded for {Days} days")]
    private partial void LogUnusedMirroredVersionRemoved(string feed, string packageId, string packageVersion, int days);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {PackageId} {PackageVersion} of feed {Feed}, beyond the feed's retention limits")]
    private partial void LogVersionBeyondLimitsRemoved(string feed, string packageId, string packageVersion);
}
