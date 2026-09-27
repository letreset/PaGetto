using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace PaGetto.Core.Audit;

public class AuditEventService : IAuditEventService
{
    private readonly IContext _context;

    public AuditEventService(IContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        _context.AuditEvents.Add(auditEvent);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<(List<AuditEvent> Events, int TotalCount)> SearchAsync(
        AuditEventFilter filter, int skip, int take, CancellationToken cancellationToken)
    {
        IQueryable<AuditEvent> query = _context.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Event))
            query = query.Where(e => e.Event == filter.Event);
        if (!string.IsNullOrWhiteSpace(filter.Actor))
            query = query.Where(e => e.Actor.Contains(filter.Actor));
        if (!string.IsNullOrWhiteSpace(filter.Feed))
            query = query.Where(e => e.Feed == filter.Feed);
        if (!string.IsNullOrWhiteSpace(filter.Target))
            query = query.Where(e => e.Target.Contains(filter.Target) || e.PackageId.Contains(filter.Target));
        if (filter.FromUtc.HasValue)
            query = query.Where(e => e.TimestampUtc >= filter.FromUtc.Value);
        if (filter.BeforeUtc.HasValue)
            query = query.Where(e => e.TimestampUtc < filter.BeforeUtc.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var events = await query
            .OrderByDescending(e => e.TimestampUtc)
            .ThenByDescending(e => e.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (events, totalCount);
    }

    public async Task<List<string>> GetEventNamesAsync(CancellationToken cancellationToken)
    {
        return await _context.AuditEvents
            .Select(e => e.Event)
            .Distinct()
            .OrderBy(e => e)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        return await _context.AuditEvents
            .Where(e => e.TimestampUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
