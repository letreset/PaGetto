using System;

namespace PaGetto.Core.Webhooks;

/// <summary>
/// A change of a package that the feed's webhook is told about.
/// </summary>
/// <param name="Event">The audit event of the change, e.g. <c>package_upload_succeeded</c>.</param>
/// <param name="FeedSlug">The feed's slug.</param>
/// <param name="PackageId">The package, as the change named it.</param>
/// <param name="PackageVersion">The version, or null for a change of a whole package.</param>
/// <param name="Actor">Who made the change.</param>
/// <param name="TimestampUtc">When the change was made.</param>
/// <param name="Advisory">The advisory of a <see cref="VulnerabilityFoundEvent"/>, otherwise null.</param>
public record WebhookEvent(
    string Event,
    string FeedSlug,
    string PackageId,
    string PackageVersion,
    string Actor,
    DateTime TimestampUtc,
    string Advisory = null)
{
    /// <summary>
    /// The event of a new critical vulnerability of a stored version.
    /// </summary>
    public const string VulnerabilityFoundEvent = "vulnerability_found";
}
