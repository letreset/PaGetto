namespace PaGetto.Core.Quarantine;

/// <summary>
/// Why a new version is stored in quarantine.
/// </summary>
/// <param name="FeedSlug">The feed's slug.</param>
/// <param name="Reason">The reason, as the admin page shows it.</param>
/// <param name="AdvisoryId">The advisory the version fails, or null.</param>
public record QuarantineDecision(string FeedSlug, string Reason, string AdvisoryId);
