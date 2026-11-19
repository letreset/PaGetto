using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Vulnerabilities;

namespace PaGetto.Core.MaliciousPackages;

/// <summary>
/// Refreshes the local copy of the malicious packages (<see cref="MaliciousPackage"/>) of each
/// ecosystem that a mirroring feed blocks them for. The first copy is read from the ecosystem's
/// <c>all.zip</c>; later refreshes read only the advisories changed since. A refresh that fails
/// keeps the copy as it was and is tried again after <see cref="RetryDelay"/>.
/// </summary>
public partial class MaliciousPackageUpdater
{
    /// <summary>
    /// How long after a failed refresh of an ecosystem it is tried again.
    /// </summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The most changed advisories read one by one; with more, the whole <c>all.zip</c> is read.
    /// </summary>
    public const int MaxChanges = 2000;

    private const int BatchSize = 500;

    // The lengths of the database columns.
    private const int MaxNameLength = 256;
    private const int MaxErrorLength = 1000;

    private readonly IContext _context;
    private readonly OsvDataClient _client;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly IOptionsMonitor<MaliciousPackageOptions> _options;
    private readonly SystemTime _time;
    private readonly ILogger<MaliciousPackageUpdater> _logger;

    public MaliciousPackageUpdater(
        IContext context,
        OsvDataClient client,
        IFeedSettingsResolver feedSettings,
        IOptionsMonitor<MaliciousPackageOptions> options,
        SystemTime time,
        ILogger<MaliciousPackageUpdater> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The ecosystems whose malicious packages are needed: those of the feeds that mirror and block them.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetEcosystemsAsync(CancellationToken cancellationToken)
    {
        var feeds = await _context.Feeds.AsNoTracking().Include(f => f.Mirrors).ToListAsync(cancellationToken);

        return feeds
            .Where(f => _feedSettings.GetBlockMaliciousPackages(f) && _feedSettings.GetMirrorOptions(f).Count > 0)
            .Select(f => OsvEcosystem.GetName(f.Type))
            .Where(e => e != null)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Refreshes each needed ecosystem whose copy is older than
    /// <see cref="MaliciousPackageOptions.RefreshIntervalHours"/>.
    /// </summary>
    /// <returns>The number of ecosystems refreshed.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = _time.UtcNow;
        var interval = TimeSpan.FromHours(_options.CurrentValue.RefreshIntervalHours);
        var refreshed = 0;

        foreach (var ecosystem in await GetEcosystemsAsync(cancellationToken))
        {
            var source = await _context.MaliciousPackageSources.FirstOrDefaultAsync(s => s.Ecosystem == ecosystem, cancellationToken);
            if (source == null)
            {
                source = new MaliciousPackageSource { Ecosystem = ecosystem };
                _context.MaliciousPackageSources.Add(source);
            }
            else if (source.RefreshedAtUtc > now - interval || source.AttemptedAtUtc > now - RetryDelay)
            {
                continue;
            }

            // Saved first, so other replicas leave this ecosystem alone while it is refreshed.
            source.AttemptedAtUtc = now;
            await _context.SaveChangesAsync(cancellationToken);

            try
            {
                await RefreshAsync(source, cancellationToken);
                source.RefreshedAtUtc = _time.UtcNow;
                source.LastError = null;
                refreshed++;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.IO.InvalidDataException && !cancellationToken.IsCancellationRequested)
            {
                // The copy stays as it was; downloads are checked against it until a refresh succeeds.
                source.LastError = Truncate(e.Message);
                LogRefreshFailed(e, ecosystem, _options.CurrentValue.DataUrl, RetryDelay);
            }

            ClearChangeTracker();
            _context.MaliciousPackageSources.Update(source);
            await _context.SaveChangesAsync(cancellationToken);
            ClearChangeTracker();
        }

        return refreshed;
    }

    private async Task RefreshAsync(MaliciousPackageSource source, CancellationToken cancellationToken)
    {
        var ecosystem = source.Ecosystem;
        if (source.CursorUtc.HasValue)
        {
            var changes = await _client.GetChangesAsync(ecosystem, source.CursorUtc.Value, MaxChanges, cancellationToken);
            if (!changes.TooMany)
            {
                foreach (var batch in changes.Ids.Chunk(BatchSize))
                {
                    var advisories = new List<MaliciousAdvisory>();
                    foreach (var id in batch)
                    {
                        advisories.Add(await _client.GetAdvisoryOrNullAsync(ecosystem, id, cancellationToken)
                            ?? MaliciousAdvisory.Removed(id));
                    }

                    await ReplaceAsync(ecosystem, advisories, cancellationToken);
                }

                source.CursorUtc = changes.NewestUtc ?? source.CursorUtc;
                LogRefreshed(ecosystem, changes.Ids.Count);
                return;
            }
        }

        source.CursorUtc = await ReadAllAsync(ecosystem, cancellationToken) ?? source.CursorUtc;
    }

    // Reads the whole archive: stores the advisories that are new or changed and removes those it no longer has.
    private async Task<DateTime?> ReadAllAsync(string ecosystem, CancellationToken cancellationToken)
    {
        var stored = (await _context.MaliciousPackages
                .AsNoTracking()
                .Where(p => p.Ecosystem == ecosystem)
                .Select(p => new { p.AdvisoryId, p.ModifiedUtc })
                .ToListAsync(cancellationToken))
            .GroupBy(p => p.AdvisoryId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(p => p.ModifiedUtc), StringComparer.Ordinal);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var changed = new List<MaliciousAdvisory>();
        DateTime? newest = null;
        var count = 0;

        await foreach (var advisory in _client.GetAllAsync(ecosystem, cancellationToken))
        {
            seen.Add(advisory.Id);
            if (newest == null || advisory.ModifiedUtc > newest)
            {
                newest = advisory.ModifiedUtc;
            }

            if (stored.TryGetValue(advisory.Id, out var modified) && modified == advisory.ModifiedUtc && !advisory.Withdrawn)
            {
                continue;
            }

            changed.Add(advisory);
            count++;
            if (changed.Count == BatchSize)
            {
                await ReplaceAsync(ecosystem, changed, cancellationToken);
                changed.Clear();
            }
        }

        await ReplaceAsync(ecosystem, changed, cancellationToken);

        foreach (var batch in stored.Keys.Where(id => !seen.Contains(id)).Chunk(BatchSize))
        {
            await _context.MaliciousPackages
                .Where(p => p.Ecosystem == ecosystem && batch.Contains(p.AdvisoryId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        LogRefreshed(ecosystem, count);
        return newest;
    }

    // Replaces the stored packages of the advisories with the packages they list now.
    private async Task ReplaceAsync(string ecosystem, List<MaliciousAdvisory> advisories, CancellationToken cancellationToken)
    {
        if (advisories.Count == 0)
        {
            return;
        }

        var ids = advisories.Select(a => a.Id).ToList();
        await _context.MaliciousPackages
            .Where(p => p.Ecosystem == ecosystem && ids.Contains(p.AdvisoryId))
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var advisory in advisories.Where(a => !a.Withdrawn))
        {
            foreach (var (packageEcosystem, name, affected) in advisory.Packages)
            {
                var normalized = MaliciousPackageName.Normalize(ecosystem, name);
                if (packageEcosystem != ecosystem || normalized.Length > MaxNameLength)
                {
                    continue;
                }

                _context.MaliciousPackages.Add(new MaliciousPackage
                {
                    Ecosystem = ecosystem,
                    Name = normalized,
                    AdvisoryId = advisory.Id,
                    Summary = advisory.Summary,
                    AffectedVersions = affected,
                    ModifiedUtc = advisory.ModifiedUtc,
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        ClearChangeTracker();
    }

    // Forgets the saved rows, so a refresh of many advisories doesn't keep them all in memory.
    private void ClearChangeTracker()
    {
        if (_context is DbContext db)
        {
            db.ChangeTracker.Clear();
        }
    }

    private static string Truncate(string text)
    {
        return text == null || text.Length <= MaxErrorLength
            ? text
            : text[..MaxErrorLength];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Malicious packages of {Ecosystem} refreshed: {Count} advisories added or changed.")]
    private partial void LogRefreshed(string ecosystem, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The malicious packages of {Ecosystem} could not be refreshed from {DataUrl}; the stored list stays in use, and the refresh is tried again after {Delay}. Set MaliciousPackages:Enabled to false on servers without access to it.")]
    private partial void LogRefreshFailed(Exception exception, string ecosystem, string dataUrl, TimeSpan delay);
}
