using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PaGetto.Core.Upstream;

public partial class DownloadsImporter
{
    private const int BatchSize = 200;

    private readonly IContext _context;
    private readonly IPackageDownloadsSource _downloadsSource;
    private readonly ILogger<DownloadsImporter> _logger;

    public DownloadsImporter(
        IContext context,
        IPackageDownloadsSource downloadsSource,
        ILogger<DownloadsImporter> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(downloadsSource);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _downloadsSource = downloadsSource;
        _logger = logger;
    }

    public async Task ImportAsync(CancellationToken cancellationToken)
    {
        var packageDownloads = await _downloadsSource.GetPackageDownloadsAsync();
        var packages = await _context.Packages.CountAsync(cancellationToken);
        var batches = (packages / BatchSize) + 1;

        for (var batch = 0; batch < batches; batch++)
        {
            LogImportingBatch(batch);

            foreach (var package in await GetBatchAsync(batch, cancellationToken))
            {
                var packageId = package.Id.ToLowerInvariant();
                var packageVersion = package.NormalizedVersionString.ToLowerInvariant();

                if (!packageDownloads.TryGetValue(packageId, out var value) ||
                    !value.ContainsKey(packageVersion))
                {
                    continue;
                }

                package.Downloads = packageDownloads[packageId][packageVersion];
            }

            await _context.SaveChangesAsync(cancellationToken);

            LogImportedBatch(batch);
        }
    }

    private Task<List<Package>> GetBatchAsync(int batch, CancellationToken cancellationToken)
        => _context.Packages
            .OrderBy(p => p.Key)
            .Skip(batch * BatchSize)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Importing batch {Batch}...")]
    private partial void LogImportingBatch(int batch);

    [LoggerMessage(Level = LogLevel.Information, Message = "Imported batch {Batch}")]
    private partial void LogImportedBatch(int batch);
}
