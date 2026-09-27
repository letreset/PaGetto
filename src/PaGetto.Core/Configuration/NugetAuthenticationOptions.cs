namespace PaGetto.Core.Configuration;

public sealed class NugetAuthenticationOptions
{
    /// <summary>
    /// Controls which authentication mechanisms are active.
    /// Required: <c>Legacy</c>, <c>Local</c>, <c>Entra</c> or <c>Hybrid</c>.
    /// </summary>
    public AuthenticationMode Mode { get; set; }

    /// <summary>
    /// Azure Entra ID (OIDC) configuration. Required when Mode is Entra or Hybrid.
    /// </summary>
    public EntraOptions Entra { get; set; }

    /// <summary>
    /// Maximum number of days a personal access token can be valid.
    /// </summary>
    public int MaxTokenExpiryDays { get; set; } = 365;

    /// <summary>
    /// Number of consecutive failed login attempts before a local account is locked out.
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// Duration in minutes that a local account remains locked after exceeding the failed login threshold.
    /// </summary>
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>
    /// How long a web sign-in lasts without activity, in minutes. Each request extends it.
    /// </summary>
    public int SessionTimeoutMinutes { get; set; } = 60;

    /// <summary>
    /// The minimum length of local account passwords, set by an administrator or by the user.
    /// </summary>
    public int MinPasswordLength { get; set; } = 12;

    /// <summary>
    /// Username and password credentials for downloading packages (used when Mode is Config).
    /// </summary>
    public NugetCredentials[] Credentials { get; set; }

    /// <summary>
    /// Api keys for pushing packages into the feed (used when Mode is Config).
    /// </summary>
    public ApiKey[] ApiKeys { get; set; }
}
