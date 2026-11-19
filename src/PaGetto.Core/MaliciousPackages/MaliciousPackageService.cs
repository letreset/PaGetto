using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;

namespace PaGetto.Core.MaliciousPackages;

/// <summary>
/// What the admin page shows of the malicious packages, and the allowances it adds and removes.
/// </summary>
public class MaliciousPackageService
{
    private readonly IContext _context;
    private readonly MaliciousPackageUpdater _updater;
    private readonly SystemTime _time;

    public MaliciousPackageService(IContext context, MaliciousPackageUpdater updater, SystemTime time)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _updater = updater ?? throw new ArgumentNullException(nameof(updater));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// The state of the copy of each ecosystem that is needed or was copied before.
    /// </summary>
    public async Task<IReadOnlyList<MaliciousPackageSourceStatus>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        var needed = await _updater.GetEcosystemsAsync(cancellationToken);
        var sources = await _context.MaliciousPackageSources.AsNoTracking().ToListAsync(cancellationToken);
        var counts = await _context.MaliciousPackages
            .AsNoTracking()
            .GroupBy(p => p.Ecosystem)
            .Select(g => new { Ecosystem = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Ecosystem, g => g.Count, cancellationToken);

        return needed
            .Union(sources.Select(s => s.Ecosystem), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(e =>
            {
                var source = sources.FirstOrDefault(s => s.Ecosystem == e);
                return new MaliciousPackageSourceStatus(
                    e,
                    needed.Contains(e),
                    counts.GetValueOrDefault(e),
                    source?.RefreshedAtUtc,
                    source?.LastError);
            })
            .ToList();
    }

    /// <summary>
    /// The listings of a package.
    /// </summary>
    public async Task<IReadOnlyList<MaliciousPackage>> FindListingsAsync(string ecosystem, string name, CancellationToken cancellationToken)
    {
        var normalized = MaliciousPackageName.Normalize(ecosystem, name?.Trim());

        return await _context.MaliciousPackages
            .AsNoTracking()
            .Where(p => p.Ecosystem == ecosystem && p.Name == normalized)
            .OrderBy(p => p.AdvisoryId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Every allowance, by ecosystem and name.
    /// </summary>
    public async Task<IReadOnlyList<MaliciousPackageAllowance>> GetAllowancesAsync(CancellationToken cancellationToken)
    {
        return await _context.MaliciousPackageAllowances
            .AsNoTracking()
            .OrderBy(a => a.Ecosystem)
            .ThenBy(a => a.Name)
            .ThenBy(a => a.Version)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Allows a package, or one version of it, on every feed of its ecosystem.
    /// </summary>
    /// <param name="ecosystem">The OSV ecosystem.</param>
    /// <param name="name">The package name, as the malicious packages data has it.</param>
    /// <param name="version">The version, or null for every version.</param>
    /// <param name="reason">Why it is allowed.</param>
    /// <param name="createdBy">The administrator's username.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    public async Task<MaliciousPackageAllowance> AddAllowanceAsync(string ecosystem, string name, string version, string reason, string createdBy, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ecosystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var allowance = new MaliciousPackageAllowance
        {
            Ecosystem = ecosystem,
            Name = MaliciousPackageName.Normalize(ecosystem, name.Trim()),
            Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim(),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            CreatedBy = createdBy,
            CreatedAtUtc = _time.UtcNow,
        };

        _context.MaliciousPackageAllowances.Add(allowance);
        await _context.SaveChangesAsync(cancellationToken);

        return allowance;
    }

    /// <summary>
    /// Removes an allowance.
    /// </summary>
    /// <returns>The removed allowance, or null if there was none with the key.</returns>
    public async Task<MaliciousPackageAllowance> RemoveAllowanceAsync(int key, CancellationToken cancellationToken)
    {
        var allowance = await _context.MaliciousPackageAllowances.FirstOrDefaultAsync(a => a.Key == key, cancellationToken);
        if (allowance == null)
        {
            return null;
        }

        _context.MaliciousPackageAllowances.Remove(allowance);
        await _context.SaveChangesAsync(cancellationToken);

        return allowance;
    }
}
