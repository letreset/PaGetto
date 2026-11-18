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
public record WebhookEvent(
    string Event,
    string FeedSlug,
    string PackageId,
    string PackageVersion,
    string Actor,
    DateTime TimestampUtc);
