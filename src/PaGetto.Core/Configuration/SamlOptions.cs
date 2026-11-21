namespace PaGetto.Core.Configuration;

/// <summary>
/// Configuration for single sign-on through a SAML 2.0 identity provider (Keycloak, Okta, AD FS,
/// Shibboleth, Entra ID, ...).
/// </summary>
public class SamlOptions
{
    /// <summary>
    /// The URL of the identity provider's metadata. Its single sign-on URL and signing certificates
    /// are read from it.
    /// </summary>
    public string MetadataUrl { get; set; }

    /// <summary>
    /// PaGetto's entity id, the name the identity provider knows it by (for example its public URL).
    /// </summary>
    public string EntityId { get; set; }

    /// <summary>
    /// The path the identity provider posts its response to (the assertion consumer service).
    /// </summary>
    public string CallbackPath { get; set; } = "/signin-saml";

    /// <summary>
    /// The attribute of the assertion whose values are matched against the groups' role values. The
    /// value <c>Admin</c> makes the user an administrator.
    /// </summary>
    public string RoleAttribute { get; set; } = "groups";

    /// <summary>
    /// The identity provider's name on the sign-in button: "Sign in with {DisplayName}".
    /// </summary>
    public string DisplayName { get; set; } = "SSO";

    /// <summary>
    /// Whether the metadata URL has to use HTTPS. The metadata holds the certificates responses are
    /// checked with, so turn this off only for an identity provider on a local test network.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}
