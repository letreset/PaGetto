namespace PaGetto.Core.Entities;

public enum AuthProvider
{
    Entra = 0,
    Local = 1,

    /// <summary>
    /// A user of the OpenID Connect provider of <c>Authentication:Oidc</c>.
    /// </summary>
    Oidc = 2,

    /// <summary>
    /// A user of the SAML identity provider of <c>Authentication:Saml</c>.
    /// </summary>
    Saml = 3,

    /// <summary>
    /// A user of the LDAP directory of <c>Authentication:Ldap</c>.
    /// </summary>
    Ldap = 4
}
