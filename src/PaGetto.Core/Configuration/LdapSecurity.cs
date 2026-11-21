namespace PaGetto.Core.Configuration;

/// <summary>
/// How the connection to the LDAP server is protected.
/// </summary>
public enum LdapSecurity
{
    /// <summary>
    /// LDAP over TLS (LDAPS), usually on port 636.
    /// </summary>
    Ssl = 0,

    /// <summary>
    /// A plain connection, usually on port 389, upgraded to TLS with StartTLS before binding.
    /// </summary>
    StartTls = 1,

    /// <summary>
    /// A plain connection: passwords cross the network unencrypted. Only for test networks.
    /// </summary>
    None = 2,
}
