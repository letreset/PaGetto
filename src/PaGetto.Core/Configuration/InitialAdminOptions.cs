namespace PaGetto.Core.Configuration;

/// <summary>
/// The local administrator created on startup when the mode is <see cref="AuthenticationMode.Local"/>
/// or <see cref="AuthenticationMode.Hybrid"/> and no administrator exists yet.
/// Bound to the <c>Authentication:InitialAdmin</c> section.
/// </summary>
public class InitialAdminOptions
{
    /// <summary>
    /// The minimum password length, the same rule Admin &gt; Accounts applies to local accounts.
    /// </summary>
    public const int MinPasswordLength = 12;

    /// <summary>
    /// The maximum username length, the same rule Admin &gt; Accounts applies to local accounts.
    /// </summary>
    public const int MaxUsernameLength = 256;

    /// <summary>
    /// The username of the initial administrator.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// The password of the initial administrator.
    /// Should be provided via environment variable or Docker secrets in production.
    /// </summary>
    public string Password { get; set; }
}
