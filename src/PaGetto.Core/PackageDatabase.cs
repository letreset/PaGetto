using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Quarantine;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;

namespace PaGetto.Core;

public class PackageDatabase : IPackageDatabase
{
    private readonly IContext _context;
    private readonly SystemTime _time;
    private readonly QuarantinePolicy _quarantine;

    public PackageDatabase(IContext context, SystemTime time)
        : this(context, time, quarantine: null)
    {
    }

    /// <param name="context">The database.</param>
    /// <param name="time">The clock.</param>
    /// <param name="quarantine">The feeds' quarantine policies, applied to added versions; null for none.</param>
    public PackageDatabase(IContext context, SystemTime time, QuarantinePolicy quarantine)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _quarantine = quarantine;
    }

    public async Task<PackageAddResult> AddAsync(Package package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);

        var version = package.Version?.ToNormalizedString() ?? package.NormalizedVersionString;
        var quarantine = _quarantine == null ? null : await _quarantine.CheckNewVersionAsync(package.FeedId, package.Id, version, cancellationToken);
        if (quarantine != null)
        {
            package.QuarantinedAtUtc = _time.UtcNow;
            package.QuarantineReason = quarantine.Reason;
        }

        try
        {
            _context.Packages.Add(package);

            await _context.SaveChangesAsync(cancellationToken);

            if (quarantine != null)
            {
                await _quarantine.RecordQuarantinedAsync(quarantine, package.Id, version, cancellationToken);
            }

            return PackageAddResult.Success;
        }
        catch (DbUpdateException e)
            when (_context.IsUniqueConstraintViolationException(e))
        {
            return PackageAddResult.PackageAlreadyExists;
        }
    }

    public async Task<bool> ExistsAsync(Guid feedId, string id, CancellationToken cancellationToken)
    {
        return await _context
            .Packages
            .Where(p => p.FeedId == feedId && p.Id == id)
            .AnyAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(Guid feedId, string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return await _context
            .Packages
            .Where(p => p.FeedId == feedId && p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString())
            .AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Package>> FindAsync(Guid feedId, string id, bool includeUnlisted, CancellationToken cancellationToken)
    {
        var query = _context.Packages
            .Include(p => p.Dependencies)
            .Include(p => p.PackageTypes)
            .Include(p => p.TargetFrameworks)
            .Where(p => p.FeedId == feedId && p.Id == id);

        if (!includeUnlisted)
        {
            query = query.Where(p => p.Listed);
        }

        return (await query.AsSingleQuery().ToListAsync(cancellationToken)).AsReadOnly();
    }

    public Task<Package> FindOrNullAsync(
        Guid feedId,
        string id,
        NuGetVersion version,
        bool includeUnlisted,
        CancellationToken cancellationToken)
    {
        var query = _context.Packages
            .Include(p => p.Dependencies)
            .Include(p => p.TargetFrameworks)
            .Where(p => p.FeedId == feedId && p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString());

        if (!includeUnlisted)
        {
            query = query.Where(p => p.Listed);
        }

        return query.AsSingleQuery().FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> UnlistPackageAsync(Guid feedId, string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return TryUpdatePackageAsync(feedId, id, version, p => p.Listed = false, cancellationToken);
    }

    public Task<bool> RelistPackageAsync(Guid feedId, string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return TryUpdatePackageAsync(feedId, id, version, p => p.Listed = true, cancellationToken);
    }

    public async Task AddDownloadAsync(Guid feedId, string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        // A single UPDATE statement, so concurrent downloads don't overwrite each other's increments.
        var now = _time.UtcNow;
        await _context.Packages
            .Where(p => p.FeedId == feedId && p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString())
            .ExecuteUpdateAsync(
                s => s.SetProperty(p => p.Downloads, p => p.Downloads + 1).SetProperty(p => p.LastDownloadedAtUtc, now),
                cancellationToken);
    }

    public async Task<bool> HardDeletePackageAsync(Guid feedId, string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        // A quarantined version can be deleted too.
        var package = await _context.Packages
            .IgnoreQueryFilters()
            .Where(p => p.FeedId == feedId && p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString())
            .Include(p => p.Dependencies)
            .Include(p => p.TargetFrameworks)
            .AsSingleQuery()
            .FirstOrDefaultAsync(cancellationToken);

        if (package == null)
        {
            return false;
        }

        _context.Packages.Remove(package);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task<bool> TryUpdatePackageAsync(
        Guid feedId,
        string id,
        NuGetVersion version,
        Action<Package> action,
        CancellationToken cancellationToken)
    {
        var package = await _context.Packages
            .Where(p => p.FeedId == feedId && p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString())
            .FirstOrDefaultAsync(cancellationToken);

        if (package != null)
        {
            action(package);
            await _context.SaveChangesAsync(cancellationToken);

            return true;
        }

        return false;
    }
}
