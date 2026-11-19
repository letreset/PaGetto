using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.MaliciousPackages;
using PaGetto.Core.Vulnerabilities;
using PaGetto.Core.Webhooks;

namespace PaGetto.Core.Quarantine;

/// <summary>
/// Applies a feed's quarantine policies (<see cref="Feed.QuarantineSeverity"/> and
/// <see cref="Feed.QuarantineMaliciousPackages"/>) to its versions. A new version that is listed as
/// malicious is quarantined when it is stored; with a vulnerability policy, a new version waits in
/// quarantine for its first scan, and every scan quarantines the versions that fail the policy. A
/// version an administrator released is left alone. Every quarantine and release is an audit event
/// and a webhook event. Scoped.
/// </summary>
public partial class QuarantinePolicy
{
    /// <summary>
    /// Who the audit log and the webhook say quarantined or released a version.
    /// </summary>
    public const string Actor = "quarantine-policy";

    /// <summary>
    /// The reason of a new version that waits for its first vulnerability scan.
    /// </summary>
    public const string ScanPendingReason = "Waiting for its first vulnerability scan.";

    /// <summary>
    /// The audit and webhook event of a version put in quarantine.
    /// </summary>
    public const string QuarantinedEvent = "package_quarantined";

    /// <summary>
    /// The audit and webhook event of a version released from quarantine.
    /// </summary>
    public const string ReleasedEvent = "package_released";

    private readonly IContext _context;
    private readonly MaliciousPackageGuard _malicious;
    private readonly WebhookQueue _webhooks;
    private readonly SystemTime _time;
    private readonly IOptionsMonitor<VulnerabilityScanningOptions> _scanning;
    private readonly ILogger<QuarantinePolicy> _logger;

    public QuarantinePolicy(
        IContext context,
        MaliciousPackageGuard malicious,
        WebhookQueue webhooks,
        SystemTime time,
        IOptionsMonitor<VulnerabilityScanningOptions> scanning,
        ILogger<QuarantinePolicy> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _malicious = malicious ?? throw new ArgumentNullException(nameof(malicious));
        _webhooks = webhooks ?? throw new ArgumentNullException(nameof(webhooks));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _scanning = scanning ?? throw new ArgumentNullException(nameof(scanning));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Decides whether a version is quarantined when it is stored.
    /// </summary>
    /// <param name="feedId">The feed the version is added to.</param>
    /// <param name="name">The package's stored name.</param>
    /// <param name="version">The version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The decision, or null when the version is served right away.</returns>
    public async Task<QuarantineDecision> CheckNewVersionAsync(Guid feedId, string name, string version, CancellationToken cancellationToken)
    {
        var feed = await _context.Feeds.AsNoTracking().FirstOrDefaultAsync(f => f.Id == feedId, cancellationToken);
        if (feed == null)
        {
            return null;
        }

        if (feed.QuarantineMaliciousPackages)
        {
            var listing = await _malicious.FindListingOrNullAsync(feed.Type, name, version, cancellationToken);
            if (listing != null)
            {
                return new QuarantineDecision(feed.Slug, $"Listed as a malicious package ({listing.AdvisoryId}).", listing.AdvisoryId);
            }
        }

        if (feed.QuarantineSeverity != null
            && _scanning.CurrentValue.Enabled
            && OsvEcosystem.SupportedFeedTypes.Contains(feed.Type))
        {
            return new QuarantineDecision(feed.Slug, ScanPendingReason, null);
        }

        return null;
    }

    /// <summary>
    /// Records that a new version was stored in quarantine.
    /// </summary>
    public Task RecordQuarantinedAsync(QuarantineDecision decision, string name, string version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);

        return RecordAsync(QuarantinedEvent, decision.FeedSlug, name, version, Actor, decision.Reason, decision.AdvisoryId, cancellationToken);
    }

    /// <summary>
    /// Applies the feeds' vulnerability policies to versions that were just scanned: a version with
    /// a finding of the feed's <see cref="Feed.QuarantineSeverity"/> or higher is quarantined, and a
    /// version that waited for this scan and has none is released.
    /// </summary>
    /// <param name="packageKeys">The scanned NuGet packages.</param>
    /// <param name="versionKeys">The scanned versions of other ecosystems.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    public async Task ApplyScanResultsAsync(IReadOnlyCollection<int> packageKeys, IReadOnlyCollection<int> versionKeys, CancellationToken cancellationToken)
    {
        var feeds = await _context.Feeds
            .AsNoTracking()
            .Where(f => f.QuarantineSeverity != null)
            .ToDictionaryAsync(f => f.Id, cancellationToken);

        var changes = new List<Change>();

        if (feeds.Count > 0 && packageKeys.Count > 0)
        {
            var feedIds = feeds.Keys.ToList();
            var packages = await _context.Packages
                .IgnoreQueryFilters()
                .Where(p => packageKeys.Contains(p.Key) && feedIds.Contains(p.FeedId) && p.QuarantineReleasedAtUtc == null)
                .ToListAsync(cancellationToken);
            var findings = await _context.PackageVulnerabilities
                .AsNoTracking()
                .Where(v => v.PackageKey != null && packageKeys.Contains(v.PackageKey.Value))
                .ToListAsync(cancellationToken);

            foreach (var package in packages)
            {
                var feed = feeds[package.FeedId];
                var version = NuGetVersion.TryParse(package.NormalizedVersionString, out var parsed) ? parsed.ToNormalizedString() : package.NormalizedVersionString;
                var change = Apply(
                    feed,
                    package.Id,
                    version,
                    findings.Where(f => f.PackageKey == package.Key),
                    package.QuarantinedAtUtc != null,
                    package.QuarantineReason,
                    (at, reason) =>
                    {
                        package.QuarantinedAtUtc = at;
                        package.QuarantineReason = reason;
                    });
                if (change != null)
                {
                    changes.Add(change);
                }
            }
        }

        if (feeds.Count > 0 && versionKeys.Count > 0)
        {
            var feedIds = feeds.Keys.ToList();
            var versions = await _context.ArtifactVersions
                .IgnoreQueryFilters()
                .Include(v => v.Artifact)
                .Where(v => versionKeys.Contains(v.Key) && feedIds.Contains(v.Artifact.FeedId) && v.QuarantineReleasedAtUtc == null)
                .ToListAsync(cancellationToken);
            var findings = await _context.PackageVulnerabilities
                .AsNoTracking()
                .Where(v => v.ArtifactVersionKey != null && versionKeys.Contains(v.ArtifactVersionKey.Value))
                .ToListAsync(cancellationToken);

            foreach (var version in versions)
            {
                var feed = feeds[version.Artifact.FeedId];
                var change = Apply(
                    feed,
                    version.Artifact.Name,
                    version.Version,
                    findings.Where(f => f.ArtifactVersionKey == version.Key),
                    version.QuarantinedAtUtc != null,
                    version.QuarantineReason,
                    (at, reason) =>
                    {
                        version.QuarantinedAtUtc = at;
                        version.QuarantineReason = reason;
                    });
                if (change != null)
                {
                    changes.Add(change with { IsArtifact = true });
                }
            }
        }

        if (changes.Count == 0)
        {
            return;
        }

        await _context.SaveChangesAsync(cancellationToken);

        foreach (var feedId in changes.Where(c => c.IsArtifact).Select(c => c.Feed.Id).Distinct())
        {
            await RaiseArtifactRevisionAsync(feedId, cancellationToken);
        }

        foreach (var change in changes)
        {
            await RecordAsync(change.Event, change.Feed.Slug, change.Name, change.Version, Actor, change.Reason, change.AdvisoryId, cancellationToken);
        }
    }

    /// <summary>
    /// Makes the indexes cached for a feed that is not a NuGet feed be built again, after one of
    /// its versions was quarantined or released.
    /// </summary>
    public async Task RaiseArtifactRevisionAsync(Guid feedId, CancellationToken cancellationToken)
    {
        await _context.Feeds
            .Where(f => f.Id == feedId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.ArtifactRevision, f => f.ArtifactRevision + 1), cancellationToken);
    }

    /// <summary>
    /// Stores an audit event about a version and sends it to the feed's webhook.
    /// </summary>
    public async Task RecordAsync(
        string eventName,
        string feedSlug,
        string name,
        string version,
        string actor,
        string detail,
        string advisoryId,
        CancellationToken cancellationToken)
    {
        var now = _time.UtcNow;
        LogEvent(eventName, name, version, feedSlug, detail ?? string.Empty);

        _context.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TimestampUtc = now,
            Event = eventName,
            Actor = actor,
            Feed = feedSlug,
            PackageId = name,
            PackageVersion = version,
            Detail = detail,
        });
        await _context.SaveChangesAsync(cancellationToken);

        _webhooks.Enqueue(new WebhookEvent(eventName, feedSlug, name, version, actor, now, advisoryId));
    }

    private Change Apply(
        Feed feed,
        string name,
        string version,
        IEnumerable<PackageVulnerability> findings,
        bool isQuarantined,
        string reason,
        Action<DateTime?, string> set)
    {
        var threshold = feed.QuarantineSeverity.Value;
        var failing = findings
            .Where(f => f.Severity >= threshold && f.Severity != VulnerabilitySeverity.Unknown)
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.AdvisoryId, StringComparer.Ordinal)
            .FirstOrDefault();

        var isPending = isQuarantined && reason == ScanPendingReason;
        if (failing != null && (!isQuarantined || isPending))
        {
            var newReason = $"Vulnerability {failing.AdvisoryId} ({failing.Severity}) is at or above the feed's {threshold} limit.";
            set(_time.UtcNow, newReason);

            return new Change(QuarantinedEvent, feed, name, version, newReason, failing.AdvisoryId, false);
        }

        if (failing == null && isPending)
        {
            set(null, null);

            return new Change(ReleasedEvent, feed, name, version, "The vulnerability scan found nothing at or above the limit.", null, false);
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Event}: {PackageId} {PackageVersion} of feed {Feed}. {Detail}")]
    private partial void LogEvent(string @event, string packageId, string packageVersion, string feed, string detail);

    private sealed record Change(string Event, Feed Feed, string Name, string Version, string Reason, string AdvisoryId, bool IsArtifact);
}
