using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Search;
using PaGetto.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Packaging;

namespace PaGetto.Core.Indexing;

public partial class PackageIndexingService : IPackageIndexingService
{
    private readonly IPackageDatabase _packages;
    private readonly IPackageStorageService _storage;
    private readonly ISearchIndexer _search;
    private readonly SystemTime _time;
    private readonly IOptionsSnapshot<PaGettoOptions> _options;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IFeedService _feedService;
    private readonly ILogger<PackageIndexingService> _logger;
    private readonly IPackageDeletionService _packageDeletionService;

    public PackageIndexingService(
        IPackageDatabase packages,
        IPackageStorageService storage,
        IPackageDeletionService packageDeletionService,
        ISearchIndexer search,
        SystemTime time,
        IOptionsSnapshot<PaGettoOptions> options,
        IFeedSettingsResolver feedSettings,
        IFeedService feedService,
        ILogger<PackageIndexingService> logger)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _feedService = feedService ?? throw new ArgumentNullException(nameof(feedService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _packageDeletionService = packageDeletionService ?? throw new ArgumentNullException(nameof(packageDeletionService));
#pragma warning disable CS0618 // Type or member is obsolete
        if (_options.Value.MaxVersionsPerPackage > 0)
        {
            LogMaxVersionsPerPackageDeprecated();
        }
#pragma warning restore CS0618 // Type or member is obsolete
    }

    public async Task<PackageIndexingResult> IndexAsync(Guid feedId, string feedSlug, Stream packageStream, string cacheFeedUrl, DateTime? published, CancellationToken cancellationToken)
    {
        // Try to extract all the necessary information from the package.
        Package package;
        Stream nuspecStream;
        Stream readmeStream;
        Stream iconStream;

        try
        {
            using var packageReader = new PackageArchiveReader(packageStream, leaveStreamOpen: true);
            package = packageReader.GetPackageMetadata();
            package.CachedFrom = cacheFeedUrl;
            package.Published = published ?? _time.UtcNow;
            package.FeedId = feedId;
            package.Size = packageStream.Length;

            nuspecStream = await packageReader.GetNuspecAsync(cancellationToken);
            nuspecStream = await nuspecStream.AsTemporaryFileStreamAsync(cancellationToken);

            if (package.HasReadme)
            {
                readmeStream = await packageReader.GetReadmeAsync(cancellationToken);
                readmeStream = await readmeStream.AsTemporaryFileStreamAsync(cancellationToken);
            }
            else
            {
                readmeStream = null;
            }

            iconStream = null;
            if (package.HasEmbeddedIcon)
            {
                try
                {
                    iconStream = await packageReader.GetIconAsync(cancellationToken);
                    iconStream = await iconStream.AsTemporaryFileStreamAsync(cancellationToken);
                }
                catch (FileNotFoundException e)
                {
                    // The nuspec declares an icon that isn't in the archive. The icon is cosmetic,
                    // so index the package as one without an embedded icon.
                    LogEmbeddedIconMissing(e, package.Id, package.NormalizedVersionString);

                    package.HasEmbeddedIcon = false;
                }
            }
        }
        catch (Exception e)
        {
            LogInvalidPackage(e);

            return PackageIndexingResult.InvalidPackage;
        }

        // The package is well-formed. Ensure this is a new package.
        var feed = await _feedService.GetFeedByIdAsync(feedId, cancellationToken);
        if (await _packages.ExistsAsync(feedId, package.Id, package.Version, cancellationToken))
        {
            var allowOverwrites = _feedSettings.GetAllowPackageOverwrites(feed);
            if (allowOverwrites == PackageOverwriteAllowed.False ||
                (allowOverwrites == PackageOverwriteAllowed.PrereleaseOnly && !package.IsPrerelease))
            {
                return PackageIndexingResult.PackageAlreadyExists;
            }

            await _packages.HardDeletePackageAsync(feedId, package.Id, package.Version, cancellationToken);
            await _storage.DeleteAsync(feedSlug, package.Id, package.Version, cancellationToken);
        }

        // TODO: Add more package validations
        // TODO: Call PackageArchiveReader.ValidatePackageEntriesAsync
        LogPackageValidated(package.Id, package.NormalizedVersionString);

        try
        {
            packageStream.Position = 0;

            await _storage.SavePackageContentAsync(
                feedSlug,
                package,
                packageStream,
                nuspecStream,
                readmeStream,
                iconStream,
                cancellationToken);
        }
        catch (Exception e)
        {
            // This may happen due to concurrent pushes.
            // TODO: Make IPackageStorageService.SavePackageContentAsync return a result enum so this
            // can be properly handled.
            LogPersistContentFailed(e, package.Id, package.NormalizedVersionString);

            throw;
        }

        LogContentPersisted(package.Id, package.NormalizedVersionString);

        var result = await _packages.AddAsync(package, cancellationToken);
        if (result == PackageAddResult.PackageAlreadyExists)
        {
            LogMetadataAlreadyExists(package.Id, package.NormalizedVersionString);

            return PackageIndexingResult.PackageAlreadyExists;
        }

        if (result != PackageAddResult.Success)
        {
            LogUnknownPackageAddResult(result);

            throw new InvalidOperationException($"Unknown {nameof(PackageAddResult)} value: {result}");
        }

        LogMetadataPersisted(package.Id, package.NormalizedVersionString);

        await _search.IndexAsync(package, cancellationToken);

        var retention = _feedSettings.GetRetentionOptions(feed);
        if (retention.MaxMajorVersions.HasValue ||
            retention.MaxMinorVersions.HasValue ||
            retention.MaxPatchVersions.HasValue ||
            retention.MaxPrereleaseVersions.HasValue)
        {
            try
            {
                LogDeletingOlderVersions(package.Id, package.NormalizedVersionString);

                var deleted = await _packageDeletionService.DeleteOldVersionsAsync(
                    feedId,
                    feedSlug,
                    package,
                    retention.MaxMajorVersions,
                    retention.MaxMinorVersions,
                    retention.MaxPatchVersions,
                    retention.MaxPrereleaseVersions,
                    cancellationToken);
                if (deleted > 0)
                {
                    LogOlderVersionsDeleted(deleted, package.Id, package.NormalizedVersionString);
                }
            }
            catch (Exception e)
            {
                LogCleanupOlderVersionsFailed(e, package.Id, package.NormalizedVersionString);
            }
        }

        LogPackageIndexed(package.Id, package.NormalizedVersionString);

        return PackageIndexingResult.Success;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "MaxVersionsPerPackage is deprecated and is not used. Please use MaxMajorVersions, MaxMinorVersions, MaxPatchVersions, and MaxPrereleaseVersions instead.")]
    private partial void LogMaxVersionsPerPackageDeprecated();

    [LoggerMessage(Level = LogLevel.Error, Message = "Uploaded package is invalid")]
    private partial void LogInvalidPackage(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Validated package {PackageId} {PackageVersion}, persisting content to storage...")]
    private partial void LogPackageValidated(string packageId, string packageVersion);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist package {PackageId} {PackageVersion} content to storage")]
    private partial void LogPersistContentFailed(Exception exception, string packageId, string packageVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Persisted package {Id} {Version} content to storage, saving metadata to database...")]
    private partial void LogContentPersisted(string id, string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Package {Id} {Version} metadata already exists in database")]
    private partial void LogMetadataAlreadyExists(string id, string version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unknown PackageAddResult value: {PackageAddResult}")]
    private partial void LogUnknownPackageAddResult(PackageAddResult packageAddResult);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully persisted package {Id} {Version} metadata to database. Indexing in search...")]
    private partial void LogMetadataPersisted(string id, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleting older packages for package {PackageId} {PackageVersion}")]
    private partial void LogDeletingOlderVersions(string packageId, string packageVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {packages} older packages for package {PackageId} {PackageVersion}")]
    private partial void LogOlderVersionsDeleted(int packages, string packageId, string packageVersion);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to cleanup older versions of package {PackageId} {PackageVersion}")]
    private partial void LogCleanupOlderVersionsFailed(Exception exception, string packageId, string packageVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully indexed package {Id} {Version} in search")]
    private partial void LogPackageIndexed(string id, string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Package {PackageId} {PackageVersion} declares an embedded icon that is missing, ignoring the icon")]
    private partial void LogEmbeddedIconMissing(Exception exception, string packageId, string packageVersion);
}
