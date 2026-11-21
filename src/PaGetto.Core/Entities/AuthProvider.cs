namespace PaGetto.Core.Entities;

public enum AuthProvider
{
    Entra = 0,
    Local = 1,

    /// <summary>
    /// A user of the OpenID Connect provider of <c>Authentication:Oidc</c>.
    /// </summary>
    Oidc = 2
}
