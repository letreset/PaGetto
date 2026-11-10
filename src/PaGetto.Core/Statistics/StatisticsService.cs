using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PaGetto.Core.Statistics;

public class StatisticsService : IStatisticsService
{
    private readonly IServiceProvider _serviceProvider;

    public StatisticsService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<int> GetPackagesTotalAmount(Guid feedId)
    {
        var (scope, dbContext) = GetDbContext();
        var packagesCount = await dbContext.Packages
            .Where(p => p.FeedId == feedId)
            .GroupBy(p => p.Id)
            .CountAsync();

        // A feed has either NuGet packages or artifacts, so one of the two counts is zero.
        packagesCount += await dbContext.Artifacts.CountAsync(a => a.FeedId == feedId);
        scope.Dispose();
        return packagesCount;
    }

    public async Task<int> GetVersionsTotalAmount(Guid feedId)
    {
        var (scope, dbContext) = GetDbContext();
        var packagesVersionsCount = await dbContext.Packages
            .Where(p => p.FeedId == feedId)
            .CountAsync();
        packagesVersionsCount += await dbContext.ArtifactVersions.CountAsync(v => v.Artifact.FeedId == feedId);
        scope.Dispose();
        return packagesVersionsCount;
    }

    public async Task<FeedStatistics> GetFeedStatisticsAsync(Guid feedId, int listSize, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IContext>();

        var feedType = await dbContext.Feeds
            .Where(f => f.Id == feedId)
            .Select(f => f.Type)
            .FirstOrDefaultAsync(cancellationToken);
        if (feedType != FeedType.NuGet)
        {
            return await GetArtifactStatisticsAsync(dbContext, feedId, listSize, cancellationToken);
        }

        var versions = dbContext.Packages.Where(p => p.FeedId == feedId);

        var totalDownloads = await versions.SumAsync(p => p.Downloads, cancellationToken);
        var totalSize = await versions.SumAsync(p => p.Size ?? 0, cancellationToken);
        var totalVersions = await versions.CountAsync(cancellationToken);
        var prereleaseVersions = await versions.CountAsync(p => p.IsPrerelease, cancellationToken);
        var unlistedVersions = await versions.CountAsync(p => !p.Listed, cancellationToken);

        var mostDownloaded = await versions
            .GroupBy(p => p.Id)
            .Select(g => new PackageDownloadCount { Id = g.Key, Downloads = g.Sum(p => p.Downloads) })
            .Where(p => p.Downloads > 0)
            .OrderByDescending(p => p.Downloads)
            .ThenBy(p => p.Id)
            .Take(listSize)
            .ToListAsync(cancellationToken);

        var recentlyPublished = await versions
            .Where(p => p.Listed)
            .OrderByDescending(p => p.Published)
            .Take(listSize)
            .Select(p => new PublishedPackageVersion { Id = p.Id, Version = p.NormalizedVersionString, Published = p.Published })
            .ToListAsync(cancellationToken);

        return new FeedStatistics
        {
            TotalDownloads = totalDownloads,
            TotalSizeBytes = totalSize,
            StableVersions = totalVersions - prereleaseVersions,
            PrereleaseVersions = prereleaseVersions,
            UnlistedVersions = unlistedVersions,
            MostDownloaded = mostDownloaded,
            RecentlyPublished = recentlyPublished,
        };
    }

    /// <summary>
    /// The statistics of a feed that is not a NuGet feed, whose packages are artifacts.
    /// </summary>
    private static async Task<FeedStatistics> GetArtifactStatisticsAsync(IContext dbContext, Guid feedId, int listSize, CancellationToken cancellationToken)
    {
        var versions = dbContext.ArtifactVersions.Where(v => v.Artifact.FeedId == feedId);

        var totalDownloads = await versions.SumAsync(v => v.Downloads, cancellationToken);
        var totalSize = await versions.SumAsync(v => v.Size, cancellationToken);
        var totalVersions = await versions.CountAsync(cancellationToken);
        var prereleaseVersions = await versions.CountAsync(v => v.IsPrerelease, cancellationToken);
        var unlistedVersions = await versions.CountAsync(v => !v.Listed, cancellationToken);

        var mostDownloaded = await versions
            .GroupBy(v => v.Artifact.Name)
            .Select(g => new PackageDownloadCount { Id = g.Key, Downloads = g.Sum(v => v.Downloads) })
            .Where(p => p.Downloads > 0)
            .OrderByDescending(p => p.Downloads)
            .ThenBy(p => p.Id)
            .Take(listSize)
            .ToListAsync(cancellationToken);

        var recentlyPublished = await versions
            .Where(v => v.Listed)
            .OrderByDescending(v => v.Published)
            .Take(listSize)
            .Select(v => new PublishedPackageVersion { Id = v.Artifact.Name, Version = v.Version, Published = v.Published })
            .ToListAsync(cancellationToken);

        return new FeedStatistics
        {
            TotalDownloads = totalDownloads,
            TotalSizeBytes = totalSize,
            StableVersions = totalVersions - prereleaseVersions,
            PrereleaseVersions = prereleaseVersions,
            UnlistedVersions = unlistedVersions,
            MostDownloaded = mostDownloaded,
            RecentlyPublished = recentlyPublished,
        };
    }

    public IEnumerable<string> GetKnownServices()
    {
        using var newScope = _serviceProvider.CreateScope();
        var configuration = newScope.ServiceProvider.GetRequiredService<IConfiguration>();
        var servicesNames = new List<string>();

        // Database providers.
        if (configuration.HasDatabaseType("MySql")) servicesNames.Add("MySql");
        if (configuration.HasDatabaseType("PostgreSql")) servicesNames.Add("PostgreSql");
        if (configuration.HasDatabaseType("SqlServer")) servicesNames.Add("SqlServer");
        if (configuration.HasDatabaseType("Sqlite")) servicesNames.Add("Sqlite");

        // Storage providers.
        if (configuration.HasStorageType("FileSystem")) servicesNames.Add("FileSystem");
        if (configuration.HasStorageType("AwsS3")) servicesNames.Add("AwsS3");
        if (configuration.HasStorageType("AliyunOss")) servicesNames.Add("AliyunOss");
        if (configuration.HasStorageType("GoogleCloud")) servicesNames.Add("GoogleCloud");
        if (configuration.HasStorageType("TencentCos")) servicesNames.Add("TencentCos");
        return servicesNames;
    }

    /// <summary>
    /// Creates a new DI scope and resolves an <see cref="IContext"/>.
    /// </summary>
    /// <remarks>Note, that the scope has to be disposed after the db queries.</remarks>
    /// <returns>A tuple of <see cref="IServiceScope"/> and <see cref="IContext"/>.</returns>
    private (IServiceScope, IContext) GetDbContext()
    {
        var newScope = _serviceProvider.CreateScope();
        var dbContext = newScope.ServiceProvider.GetRequiredService<IContext>();

        return (newScope, dbContext);
    }
}
