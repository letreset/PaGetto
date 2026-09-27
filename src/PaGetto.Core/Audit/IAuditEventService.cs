using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Audit;

/// <summary>
/// Stores the audit events shown on the admin audit page.
/// </summary>
public interface IAuditEventService
{
    Task AddAsync(AuditEvent auditEvent, CancellationToken cancellationToken);

    /// <summary>
    /// The matching events, newest first, and how many match in total.
    /// </summary>
    Task<(List<AuditEvent> Events, int TotalCount)> SearchAsync(
        AuditEventFilter filter, int skip, int take, CancellationToken cancellationToken);

    /// <summary>
    /// The distinct event names, sorted.
    /// </summary>
    Task<List<string>> GetEventNamesAsync(CancellationToken cancellationToken);

    /// <returns>The number of deleted events.</returns>
    Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken);
}
