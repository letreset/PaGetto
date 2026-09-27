using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Indexing;
using PaGetto.Core.Upstream;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace PaGetto.Core;

public partial class PackageService : IPackageService
{
    private readonly IPackageDatabase _db;
    private readonly IUpstreamClientFactory _upstreamFactory;
    private readonly IFeedService _feedService;
    private readonly IPackageIndexingService _indexer;
    private readonly PackageMirrorLock _mirrorLock;
    private readonly ILogger<PackageService> _logger;

    public PackageService(
        IPackageDatabase db,
        IUpstreamClientFactory upstreamFactory,
        IFeedService feedService,
        IPackageIndexingService indexer,
        PackageMirrorLock mirrorLock,
        ILogger<PackageService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _upstreamFactory = upstreamFactory ?? throw new ArgumentNullException(nameof(upstreamFactory));
        _feedService = feedService ?? throw new ArgumentNullException(nameof(feedService));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _mirrorLock = mirrorLock ?? throw new ArgumentNullException(nameof(mirrorLock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<NuGetVersion>> FindPackageVersionsAsync(
        Guid feedId,
        string id,
        CancellationToken cancellationToken)
    {
        var feed = await _feedService.GetFeedByIdAsync(feedId, cancellationToken);
        var upstream = _upstreamFactory.CreateForFeed(feed);
        var upstreamVersions = await upstream.ListPackageVersionsAsync(id, cancellationToken);

        // Merge the local package versions into the upstream package versions.
        var localPackages = await _db.FindAsync(feedId, id, includeUnlisted: true, cancellationToken);
        var localVersions = localPackages.Select(p => p.Version);

        if (!upstreamVersions.Any()) return localVersions.ToList();
        if (!localPackages.Any()) return upstreamVersions;

        return upstreamVersions.Concat(localVersions).Distinct().ToList();
    }

    public async Task<IReadOnlyList<Package>> FindPackagesAsync(Guid feedId, string id, CancellationToken cancellationToken)
    {
        var feed = await _feedService.GetFeedByIdAsync(feedId, cancellationToken);
        var upstream = _upstreamFactory.CreateForFeed(feed);
        var upstreamPackages = await upstream.ListPackagesAsync(id, cancellationToken);
        var localPackages = await _db.FindAsync(feedId, id, includeUnlisted: true, cancellationToken);

        if (!upstreamPackages.Any()) return localPackages;
        if (!localPackages.Any()) return upstreamPackages;

        // Merge the local packages into the upstream packages.
        var result = upstreamPackages.ToDictionary(p => p.Version);
        var local = localPackages.ToDictionary(p => p.Version);

        foreach (var localPackage in local)
        {
            result[localPackage.Key] = localPackage.Value;
        }

        return result.Values.ToList();
    }

    public async Task<Package> FindPackageOrNullAsync(
        Guid feedId,
        string feedSlug,
        string id,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        if (!await MirrorAsync(feedId, feedSlug, id, version, cancellationToken))
        {
            return null;
        }

        return await _db.FindOrNullAsync(feedId, id, version, includeUnlisted: true, cancellationToken);
    }

    public async Task<bool> ExistsAsync(Guid feedId, string feedSlug, string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return await MirrorAsync(feedId, feedSlug, id, version, cancellationToken);
    }

    public async Task AddDownloadAsync(Guid feedId, string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        await _db.AddDownloadAsync(feedId, packageId, version, cancellationToken);
    }

    /// <summary>
    /// Index the package from an upstream if it does not exist locally.
    /// </summary>
    /// <param name="feedId">The feed's id.</param>
    /// <param name="feedSlug">The feed's slug, used to prefix storage paths.</param>
    /// <param name="id">The package ID to index from an upstream.</param>
    /// <param name="version">The package version to index from an upstream.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>True if the package exists locally or was indexed from an upstream source.</returns>
    private async Task<bool> MirrorAsync(Guid feedId, string feedSlug, string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (await _db.ExistsAsync(feedId, id, version, cancellationToken))
        {
            return true;
        }

        using var mirrorLock = await _mirrorLock.AcquireAsync(feedId, id, version, cancellationToken);

        // A concurrent request may have mirrored the package while we waited for the lock.
        if (await _db.ExistsAsync(feedId, id, version, cancellationToken))
        {
            return true;
        }

        var feed = await _feedService.GetFeedByIdAsync(feedId, cancellationToken);
        var upstream = _upstreamFactory.CreateForFeed(feed);

        var upstreamUrl = upstream.GetServiceIndexUrl();
        LogCheckingUpstream(id, version, upstreamUrl);

        try
        {
            using var packageStream = await upstream.DownloadPackageOrNullAsync(id, version, cancellationToken);
            if (packageStream == null)
            {
                LogUpstreamPackageNotFound(id, version);
                return false;
            }

            // Read after the download: with several upstreams, this is the one that served the package.
            var cacheFeedUrl = upstream.GetServiceIndexUrl();

            LogPackageDownloaded(id, version, cacheFeedUrl);

            // Keep the upstream publish date rather than the time of mirroring. The listing is
            // usually cached already (the package page and clients list versions first).
            // Upstreams report 1900-01-01 or no date at all for versions they have no date for.
            var upstreamPackages = await upstream.ListPackagesAsync(id, cancellationToken);
            var published = upstreamPackages
                .Where(p => p.Version == version && p.Published.Year > 1900)
                .Select(p => (DateTime?)p.Published)
                .FirstOrDefault();

            var result = await _indexer.IndexAsync(feedId, feedSlug, packageStream, cacheFeedUrl, published, cancellationToken);

            LogUpstreamIndexingFinished(id, version, result);

            // PackageAlreadyExists means the package is present locally (indexed by another
            // instance or pushed meanwhile), so it is available rather than a failure.
            return result == PackageIndexingResult.Success
                || result == PackageIndexingResult.PackageAlreadyExists;
        }
        catch (Exception e)
        {
            LogUpstreamIndexingFailed(e, id, version);

            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Package {PackageId} {PackageVersion} does not exist locally. Checking upstream feed ({cacheFeedUrl})...")]
    private partial void LogCheckingUpstream(string packageId, NuGetVersion packageVersion, string cacheFeedUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream feed does not have package {PackageId} {PackageVersion}")]
    private partial void LogUpstreamPackageNotFound(string packageId, NuGetVersion packageVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded package {PackageId} {PackageVersion} from {cacheFeedUrl}, indexing...")]
    private partial void LogPackageDownloaded(string packageId, NuGetVersion packageVersion, string cacheFeedUrl);

    [LoggerMessage(Level = LogLevel.Information, Message = "Finished indexing package {PackageId} {PackageVersion} from upstream feed with result {Result}")]
    private partial void LogUpstreamIndexingFinished(string packageId, NuGetVersion packageVersion, PackageIndexingResult result);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to index package {PackageId} {PackageVersion} from upstream")]
    private partial void LogUpstreamIndexingFailed(Exception exception, string packageId, NuGetVersion packageVersion);
}
