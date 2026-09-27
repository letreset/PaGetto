using System;

namespace PaGetto.Core.Entities;

/// <summary>
/// A successful change recorded for the admin audit page: an administration change in the web UI
/// (target and detail) or a package action in the web UI or the NuGet API (feed, package id and version).
/// </summary>
public class AuditEvent
{
    public Guid Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    /// <summary>
    /// The event name, e.g. <c>account_disabled</c> or <c>package_upload_succeeded</c>.
    /// </summary>
    public string Event { get; set; }

    /// <summary>
    /// The username of who made the change, the user id when there is no name, or <c>api-key</c>.
    /// </summary>
    public string Actor { get; set; }

    public string IpAddress { get; set; }

    /// <summary>
    /// The feed slug, for package events.
    /// </summary>
    public string Feed { get; set; }

    /// <summary>
    /// What an administration change was made to: a username, group name or feed slug.
    /// </summary>
    public string Target { get; set; }

    /// <summary>
    /// How an administration change was made, e.g. <c>name=Developers</c>.
    /// </summary>
    public string Detail { get; set; }

    public string PackageId { get; set; }

    public string PackageVersion { get; set; }
}
