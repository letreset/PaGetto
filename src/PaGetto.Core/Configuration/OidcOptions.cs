namespace PaGetto.Core.Configuration;

/// <summary>
/// Configuration for single sign-on through any OpenID Connect provider (Keycloak, Okta, Auth0,
/// Google, GitLab, ...).
/// </summary>
public class OidcOptions
{
    /// <summary>
    /// The issuer URL of the provider. Its discovery document is read from
    /// <c>{Authority}/.well-known/openid-configuration</c>.
    /// </summary>
    public string Authority { get; set; }

    /// <summary>
    /// The client id registered at the provider.
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    /// The client secret, for a confidential client. Should be provided via environment variable or
    /// Docker secrets in production.
    /// </summary>
    public string ClientSecret { get; set; }

    /// <summary>
    /// The scopes to request, separated by spaces. Must include <c>openid</c>.
    /// </summary>
    public string Scopes { get; set; } = "openid profile email";

    /// <summary>
    /// The callback path for the OpenID Connect sign-in response.
    /// </summary>
    public string CallbackPath { get; set; } = "/signin-oidc";

    /// <summary>
    /// The claim of the ID token whose values are matched against the groups' role values, for
    /// example <c>groups</c> or <c>roles</c>. The value <c>Admin</c> makes the user an administrator.
    /// </summary>
    public string RoleClaim { get; set; } = "groups";

    /// <summary>
    /// The provider's name on the sign-in button: "Sign in with {DisplayName}".
    /// </summary>
    public string DisplayName { get; set; } = "SSO";

    /// <summary>
    /// Whether the authority has to use HTTPS. Turn off only for a provider on a local test network.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}
