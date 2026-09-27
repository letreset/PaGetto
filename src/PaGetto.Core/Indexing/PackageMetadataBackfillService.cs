using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;

namespace PaGetto.Core.Indexing;

/// <summary>
/// Fills <see cref="Package.Size"/>, <see cref="Package.Copyright"/> and
/// <see cref="Package.LicenseExpression"/> for packages stored before they were recorded, by
/// re-reading their stored .nupkg. Runs once in the background after startup; a package whose
/// .nupkg can't be read keeps a null size and is tried again on the next start.
/// </summary>
public partial class PackageMetadataBackfillService : BackgroundService
{
    private const int BatchSize = 100;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PackageMetadataBackfillService> _logger;

    public PackageMetadataBackfillService(
        IServiceProvider serviceProvider,
        ILogger<PackageMetadataBackfillService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var filled = await BackfillAsync(stoppingToken);
            if (filled > 0)
            {
                LogBackfillCompleted(filled);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            LogBackfillFailed(e);
        }
    }

    /// <summary>
    /// Runs one pass over every package without a size.
    /// </summary>
    /// <returns>The number of packages that were filled.</returns>
    public async Task<int> BackfillAsync(CancellationToken cancellationToken)
    {
        var filled = 0;
        var lastKey = 0;

        while (true)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IPackageStorageService>();

            // Walking by key visits each package once per pass, also the ones that fail.
            var batch = await context.Packages
                .Include(p => p.Feed)
                .Where(p => p.Size == null && p.Key > lastKey)
                .OrderBy(p => p.Key)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0) return filled;

            foreach (var package in batch)
            {
                lastKey = package.Key;

                try
                {
                    await using var stored = await storage.GetPackageStreamAsync(package.Feed.Slug, package.Id, package.Version, cancellationToken);
                    await using var packageStream = await stored.AsTemporaryFileStreamAsync(cancellationToken);
                    using var reader = new PackageArchiveReader(packageStream, leaveStreamOpen: true);
                    var metadata = reader.GetPackageMetadata();

                    package.Size = packageStream.Length;
                    package.Copyright ??= metadata.Copyright;
                    package.LicenseExpression ??= metadata.LicenseExpression;
                    filled++;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    LogPackageSkipped(e, package.Feed.Slug, package.Id, package.NormalizedVersionString);
                }
            }

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException e)
            {
                // E.g. a concurrent download count update; those rows are retried on the next start.
                LogBatchNotSaved(e);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Filled size, copyright and license expression for {Count} stored packages")]
    private partial void LogBackfillCompleted(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Filling size, copyright and license expression of stored packages failed")]
    private partial void LogBackfillFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the stored package {Feed}/{PackageId} {PackageVersion} to fill its size, copyright and license expression")]
    private partial void LogPackageSkipped(Exception exception, string feed, string packageId, string packageVersion);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save a batch of filled package metadata; it is retried on the next start")]
    private partial void LogBatchNotSaved(Exception exception);
}
