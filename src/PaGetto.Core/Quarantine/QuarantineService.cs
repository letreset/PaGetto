using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Storage;

namespace PaGetto.Core.Quarantine;

/// <summary>
/// Lists the quarantined versions of every feed, and releases or deletes them for an
/// administrator. Scoped.
/// </summary>
public class QuarantineService
{
    /// <summary>
    /// The audit and webhook event of a quarantined version an administrator deleted.
    /// </summary>
    public const string DeletedEvent = "package_quarantine_deleted";

    private readonly IContext _context;
    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _artifactStorage;
    private readonly IPackageDatabase _packages;
    private readonly IPackageStorageService _packageStorage;
    private readonly QuarantinePolicy _policy;
    private readonly SystemTime _time;

    public QuarantineService(
        IContext context,
        IArtifactDatabase artifacts,
        IArtifactStorageService artifactStorage,
        IPackageDatabase packages,
        IPackageStorageService packageStorage,
        QuarantinePolicy policy,
        SystemTime time)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _artifactStorage = artifactStorage ?? throw new ArgumentNullException(nameof(artifactStorage));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _packageStorage = packageStorage ?? throw new ArgumentNullException(nameof(packageStorage));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// The quarantined versions of every feed, the most recently quarantined first.
    /// </summary>
    public async Task<IReadOnlyList<QuarantinedVersion>> ListAsync(CancellationToken cancellationToken)
    {
        var feeds = await _context.Feeds.AsNoTracking().ToDictionaryAsync(f => f.Id, cancellationToken);

        var packages = await _context.Packages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.QuarantinedAtUtc != null)
            .Select(p => new { p.Key, p.FeedId, p.Id, p.NormalizedVersionString, p.QuarantineReason, p.QuarantinedAtUtc, p.CachedFrom })
            .ToListAsync(cancellationToken);

        var versions = await _context.ArtifactVersions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(v => v.QuarantinedAtUtc != null)
            .Select(v => new { v.Key, v.Artifact.FeedId, v.Artifact.Name, v.Version, v.QuarantineReason, v.QuarantinedAtUtc, v.CachedFrom })
            .ToListAsync(cancellationToken);

        var result = new List<QuarantinedVersion>();
        foreach (var p in packages.Where(p => feeds.ContainsKey(p.FeedId)))
        {
            var version = NuGetVersion.TryParse(p.NormalizedVersionString, out var parsed) ? parsed.ToNormalizedString() : p.NormalizedVersionString;
            result.Add(new QuarantinedVersion(true, p.Key, feeds[p.FeedId], p.Id, version, p.QuarantineReason, p.QuarantinedAtUtc.Value, p.CachedFrom));
        }

        foreach (var v in versions.Where(v => feeds.ContainsKey(v.FeedId)))
        {
            result.Add(new QuarantinedVersion(false, v.Key, feeds[v.FeedId], v.Name, v.Version, v.QuarantineReason, v.QuarantinedAtUtc.Value, v.CachedFrom));
        }

        return result
            .OrderByDescending(q => q.QuarantinedAtUtc)
            .ThenBy(q => q.Feed.Slug, StringComparer.Ordinal)
            .ThenBy(q => q.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Releases a quarantined version: it is served again, and the feed's policies are no longer
    /// applied to it.
    /// </summary>
    /// <param name="isNuGet">Whether <paramref name="key"/> is a NuGet package's key, otherwise an artifact version's.</param>
    /// <param name="key">The version's key.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The released version, or null when no such version is quarantined.</returns>
    public async Task<QuarantinedVersion> ReleaseAsync(bool isNuGet, int key, string actor, CancellationToken cancellationToken)
    {
        var entry = await FindAsync(isNuGet, key, cancellationToken);
        if (entry == null)
        {
            return null;
        }

        var now = _time.UtcNow;
        if (isNuGet)
        {
            await _context.Packages
                .IgnoreQueryFilters()
                .Where(p => p.Key == key)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(p => p.QuarantinedAtUtc, (DateTime?)null)
                        .SetProperty(p => p.QuarantineReason, (string)null)
                        .SetProperty(p => p.QuarantineReleasedAtUtc, now),
                    cancellationToken);
        }
        else
        {
            await _context.ArtifactVersions
                .IgnoreQueryFilters()
                .Where(v => v.Key == key)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(v => v.QuarantinedAtUtc, (DateTime?)null)
                        .SetProperty(v => v.QuarantineReason, (string)null)
                        .SetProperty(v => v.QuarantineReleasedAtUtc, now),
                    cancellationToken);
            await _policy.RaiseArtifactRevisionAsync(entry.Feed.Id, cancellationToken);
        }

        await _policy.RecordAsync(QuarantinePolicy.ReleasedEvent, entry.Feed.Slug, entry.Name, entry.Version, actor, "Released by an administrator. Reason was: " + entry.Reason, null, cancellationToken);

        return entry;
    }

    /// <summary>
    /// Deletes a quarantined version with its content.
    /// </summary>
    /// <returns>The deleted version, or null when no such version is quarantined.</returns>
    public async Task<QuarantinedVersion> DeleteAsync(bool isNuGet, int key, string actor, CancellationToken cancellationToken)
    {
        var entry = await FindAsync(isNuGet, key, cancellationToken);
        if (entry == null)
        {
            return null;
        }

        if (isNuGet)
        {
            var version = NuGetVersion.Parse(entry.Version);
            if (await _packages.HardDeletePackageAsync(entry.Feed.Id, entry.Name, version, cancellationToken))
            {
                await _packageStorage.DeleteAsync(entry.Feed.Slug, entry.Name, version, cancellationToken);
            }
        }
        else
        {
            // Listed before the version and its file rows are gone.
            var files = await _context.ArtifactFiles
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(f => f.ArtifactVersionKey == key)
                .Select(f => f.FileName)
                .ToListAsync(cancellationToken);

            if (await _artifacts.HardDeleteVersionAsync(entry.Feed.Id, entry.Name, entry.Version, cancellationToken))
            {
                await _artifactStorage.DeleteAsync(entry.Feed.Slug, entry.Name, entry.Version, cancellationToken);
                foreach (var file in files)
                {
                    await _artifactStorage.DeleteFileAsync(entry.Feed.Slug, entry.Name, entry.Version, file, cancellationToken);
                }
            }
        }

        await _policy.RecordAsync(DeletedEvent, entry.Feed.Slug, entry.Name, entry.Version, actor, "Deleted from quarantine. Reason was: " + entry.Reason, null, cancellationToken);

        return entry;
    }

    private async Task<QuarantinedVersion> FindAsync(bool isNuGet, int key, CancellationToken cancellationToken)
    {
        var all = await ListAsync(cancellationToken);

        return all.FirstOrDefault(q => q.IsNuGet == isNuGet && q.Key == key);
    }
}
