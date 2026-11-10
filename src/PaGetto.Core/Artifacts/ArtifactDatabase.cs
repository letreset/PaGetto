using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;

namespace PaGetto.Core.Artifacts;

/// <remarks>
/// Reads don't track what they return: listing and download counts are changed with single UPDATE
/// statements, which tracked entities would not see.
/// </remarks>
public class ArtifactDatabase : IArtifactDatabase
{
    private readonly IContext _context;
    private readonly SystemTime _time;

    public ArtifactDatabase(IContext context, SystemTime time)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public async Task<IReadOnlyList<ArtifactVersion>> FindVersionsAsync(Guid feedId, string name, CancellationToken cancellationToken)
    {
        return await _context.ArtifactVersions
            .AsNoTracking()
            .Include(v => v.Artifact)
            .Where(v => v.Artifact.FeedId == feedId && v.Artifact.Name == name)
            .ToListAsync(cancellationToken);
    }

    public async Task<ArtifactVersion> FindVersionOrNullAsync(Guid feedId, string name, string version, CancellationToken cancellationToken)
    {
        return await Versions(feedId, name, version)
            .AsNoTracking()
            .Include(v => v.Artifact)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ArtifactAddResult> AddVersionAsync(Guid feedId, string name, ArtifactVersion version, CancellationToken cancellationToken)
    {
        version.NormalizedVersion = Normalize(version.Version);

        // The second attempt is for two first versions of one package added at the same time: the
        // loser fails on the package's unique index, and then finds the package the winner created.
        for (var attempt = 0; ; attempt++)
        {
            var artifact = await _context.Artifacts
                .FirstOrDefaultAsync(a => a.FeedId == feedId && a.Name == name, cancellationToken);

            var isNewArtifact = artifact == null;
            if (isNewArtifact)
            {
                artifact = new Artifact { FeedId = feedId, Name = name, CreatedAtUtc = _time.UtcNow };
                _context.Artifacts.Add(artifact);
            }

            version.Artifact = artifact;
            _context.ArtifactVersions.Add(version);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);

                return ArtifactAddResult.Success;
            }
            catch (DbUpdateException e) when (_context.IsUniqueConstraintViolationException(e))
            {
                // Forget the failed rows, so the context can be used again.
                _context.ArtifactVersions.Entry(version).State = EntityState.Detached;
                if (isNewArtifact)
                {
                    _context.Artifacts.Entry(artifact).State = EntityState.Detached;
                }

                if (!isNewArtifact || attempt > 0)
                {
                    return ArtifactAddResult.AlreadyExists;
                }
            }
        }
    }

    public async Task<bool> SetListedAsync(Guid feedId, string name, string version, bool listed, CancellationToken cancellationToken)
    {
        var updated = await Versions(feedId, name, version)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.Listed, listed), cancellationToken);

        return updated > 0;
    }

    public async Task<bool> HardDeleteVersionAsync(Guid feedId, string name, string version, CancellationToken cancellationToken)
    {
        var artifact = await _context.Artifacts
            .Include(a => a.Versions)
            .FirstOrDefaultAsync(a => a.FeedId == feedId && a.Name == name, cancellationToken);

        var normalizedVersion = Normalize(version);
        var found = artifact?.Versions.FirstOrDefault(v => v.NormalizedVersion == normalizedVersion);
        if (found == null)
        {
            return false;
        }

        if (artifact.Versions.Count == 1)
        {
            // Its versions are removed with it.
            _context.Artifacts.Remove(artifact);
        }
        else
        {
            _context.ArtifactVersions.Remove(found);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task AddDownloadAsync(Guid feedId, string name, string version, CancellationToken cancellationToken)
    {
        // A single UPDATE statement, so concurrent downloads don't overwrite each other's increments.
        await Versions(feedId, name, version)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.Downloads, v => v.Downloads + 1), cancellationToken);
    }

    public async Task<ArtifactSearchResult> SearchAsync(Guid feedId, string query, int skip, int take, CancellationToken cancellationToken)
    {
        var artifacts = _context.Artifacts.AsNoTracking().Where(a => a.FeedId == feedId);

        if (string.IsNullOrWhiteSpace(query))
        {
            artifacts = artifacts.OrderBy(a => a.Name);
        }
        else
        {
            // Names are stored lowercased.
            var q = query.Trim().ToLowerInvariant();

#pragma warning disable CA1862 // Not for EF queries: StringComparison overloads aren't translated to SQL.
            artifacts = artifacts
                .Where(a => a.Name.Contains(q) || a.Versions.Any(v => v.Description.ToLower().Contains(q)))
                .OrderBy(a => a.Name == q ? 0 : a.Name.StartsWith(q) ? 1 : a.Name.Contains(q) ? 2 : 3)
                .ThenBy(a => a.Name);
#pragma warning restore CA1862
        }

        var totalHits = await artifacts.CountAsync(cancellationToken);
        var page = await artifacts
            .Skip(skip)
            .Take(take)
            .Include(a => a.Versions)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return new ArtifactSearchResult { TotalHits = totalHits, Artifacts = page };
    }

    private IQueryable<ArtifactVersion> Versions(Guid feedId, string name, string version)
    {
        var normalizedVersion = Normalize(version);

        return _context.ArtifactVersions
            .Where(v => v.Artifact.FeedId == feedId && v.Artifact.Name == name)
            .Where(v => v.NormalizedVersion == normalizedVersion);
    }

    private static string Normalize(string version)
    {
        return version.ToLowerInvariant();
    }
}
