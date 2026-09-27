using System;
using System.Collections.Generic;

namespace PaGetto.Core.Entities;

public class User
{
    private string _username;

    public Guid Id { get; set; }

    public string Username
    {
        get => _username;
        set
        {
            _username = value;
            NormalizedUsername = NormalizeUsername(value);
        }
    }

    /// <summary>
    /// <see cref="Username"/> in upper case. Its unique index keeps usernames unique regardless of
    /// case on every database. Set together with <see cref="Username"/>.
    /// </summary>
    public string NormalizedUsername { get; set; }

    public string DisplayName { get; set; }
    public AuthProvider AuthProvider { get; set; }
    public string EntraObjectId { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsAdmin { get; set; }
    public bool CanLoginToUI { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public User CreatedByUser { get; set; }
    public List<PersonalAccessToken> PersonalAccessTokens { get; set; }
    public List<UserGroup> UserGroups { get; set; }

    public static string NormalizeUsername(string username)
    {
        return username?.ToUpperInvariant();
    }
}
