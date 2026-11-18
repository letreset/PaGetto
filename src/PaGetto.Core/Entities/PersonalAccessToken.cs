using System;

namespace PaGetto.Core.Entities;

public class PersonalAccessToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; }
    public string TokenHash { get; set; }
    public string TokenPrefix { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public bool IsRevoked { get; set; }

    /// <summary>
    /// Whether the token may publish, as far as its user may. A token can always read what its
    /// user can read.
    /// </summary>
    public bool CanPush { get; set; } = true;

    /// <summary>
    /// Whether the token may unlist and delete packages, as far as its user may.
    /// </summary>
    public bool CanDelete { get; set; } = true;

    /// <summary>
    /// The only feed the token may be used with, or null for every feed its user can use.
    /// </summary>
    public Guid? FeedId { get; set; }

    /// <summary>
    /// The smallest expiry-notification threshold (in days before expiry) that has
    /// already been emailed for this token, or <c>null</c> if none has been sent yet.
    /// Used by the expiry-notification scanner to avoid re-sending the same warning.
    /// </summary>
    public int? ExpiryNotificationThresholdDays { get; set; }

    public User User { get; set; }
}
