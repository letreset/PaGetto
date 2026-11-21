using PaGetto.Core.Entities;

namespace PaGetto.Core.Configuration;

public sealed class NugetAuthenticationOptions
{
    /// <summary>
    /// Controls which authentication mechanisms are active.
    /// Required: <c>Legacy</c>, <c>Local</c>, <c>Entra</c>, <c>Oidc</c>, <c>Saml</c> or <c>Hybrid</c>.
    /// </summary>
    public AuthenticationMode Mode { get; set; }

    /// <summary>
    /// Azure Entra ID (OIDC) configuration. Required when Mode is Entra, or Hybrid without
    /// <see cref="Oidc"/> or <see cref="Saml"/>.
    /// </summary>
    public EntraOptions Entra { get; set; }

    /// <summary>
    /// OpenID Connect provider configuration. Required when Mode is Oidc; in Hybrid mode it
    /// replaces Entra ID.
    /// </summary>
    public OidcOptions Oidc { get; set; }

    /// <summary>
    /// SAML 2.0 identity provider configuration. Required when Mode is Saml; in Hybrid mode it
    /// replaces Entra ID.
    /// </summary>
    public SamlOptions Saml { get; set; }

    /// <summary>
    /// LDAP directory configuration. In Local and Hybrid mode, directory users sign in with their
    /// directory username and password next to local accounts.
    /// </summary>
    public LdapOptions Ldap { get; set; }

    /// <summary>
    /// The provider users sign in with besides local accounts, or null when there is none
    /// (<c>Local</c> and <c>Legacy</c>).
    /// </summary>
    public AuthProvider? SingleSignOnProvider => Mode switch
    {
        AuthenticationMode.Entra => AuthProvider.Entra,
        AuthenticationMode.Oidc => AuthProvider.Oidc,
        AuthenticationMode.Saml => AuthProvider.Saml,
        AuthenticationMode.Hybrid when Oidc != null => AuthProvider.Oidc,
        AuthenticationMode.Hybrid when Saml != null => AuthProvider.Saml,
        AuthenticationMode.Hybrid => AuthProvider.Entra,
        _ => null,
    };

    /// <summary>
    /// The name of the <see cref="SingleSignOnProvider"/> in the web UI ("Sign in with ..."), or
    /// null when there is none.
    /// </summary>
    public string SingleSignOnName => SingleSignOnProvider switch
    {
        AuthProvider.Entra => "Microsoft",
        AuthProvider.Oidc => Oidc?.DisplayName ?? "SSO",
        AuthProvider.Saml => Saml?.DisplayName ?? "SSO",
        _ => null,
    };

    /// <summary>
    /// Whether local accounts can sign in (<c>Local</c> and <c>Hybrid</c>).
    /// </summary>
    public bool IsLocalEnabled => Mode is AuthenticationMode.Local or AuthenticationMode.Hybrid;

    /// <summary>
    /// Whether directory users sign in through <see cref="Ldap"/>: it is set, and the mode accepts
    /// local accounts, next to which the directory works.
    /// </summary>
    public bool IsLdapEnabled => Ldap != null && IsLocalEnabled;

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
