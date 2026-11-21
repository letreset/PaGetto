namespace PaGetto.Core.Configuration;

/// <summary>
/// Configuration for signing in with the username and password of an LDAP directory (OpenLDAP,
/// Active Directory, FreeIPA, ...), in the web UI and from client tools, next to local accounts.
/// </summary>
public class LdapOptions
{
    /// <summary>
    /// The host name of the LDAP server.
    /// </summary>
    public string Server { get; set; }

    /// <summary>
    /// The port of the LDAP server, or 0 for the default of <see cref="Security"/>: 636 for
    /// <see cref="LdapSecurity.Ssl"/>, 389 otherwise.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// How the connection is protected. The server's certificate has to be trusted by the machine.
    /// </summary>
    public LdapSecurity Security { get; set; } = LdapSecurity.Ssl;

    /// <summary>
    /// The DN of the account PaGetto searches the directory with, or empty to search anonymously.
    /// </summary>
    public string BindDn { get; set; }

    /// <summary>
    /// The password of <see cref="BindDn"/>. Should be provided via environment variable or Docker
    /// secrets in production.
    /// </summary>
    public string BindPassword { get; set; }

    /// <summary>
    /// The DN users are searched under, e.g. <c>ou=people,dc=example,dc=com</c>.
    /// </summary>
    public string SearchBase { get; set; }

    /// <summary>
    /// The filter that finds a user by the name typed at sign-in, which replaces <c>{0}</c>.
    /// Active Directory: <c>(&amp;(objectClass=user)(sAMAccountName={0}))</c>.
    /// </summary>
    public string UserFilter { get; set; } = "(&(objectClass=person)(uid={0}))";

    /// <summary>
    /// The attribute that holds the PaGetto username. Active Directory: <c>sAMAccountName</c>.
    /// </summary>
    public string UsernameAttribute { get; set; } = "uid";

    /// <summary>
    /// The attribute with the user's permanent id, which links the directory user to its PaGetto
    /// account even after a rename or move. Active Directory: <c>objectGUID</c>.
    /// </summary>
    public string IdAttribute { get; set; } = "entryUUID";

    /// <summary>
    /// The attribute with the user's display name. The common name (<c>cn</c>) is used without it.
    /// </summary>
    public string DisplayNameAttribute { get; set; } = "displayName";

    /// <summary>
    /// The attribute with the user's email address.
    /// </summary>
    public string EmailAttribute { get; set; } = "mail";

    /// <summary>
    /// The attribute of the user that lists the DNs of its groups. Used unless
    /// <see cref="GroupSearchFilter"/> is set.
    /// </summary>
    public string GroupAttribute { get; set; } = "memberOf";

    /// <summary>
    /// A filter that finds the user's groups, for directories without <c>memberOf</c>. <c>{0}</c>
    /// is replaced with the user's DN, e.g. <c>(&amp;(objectClass=groupOfNames)(member={0}))</c>.
    /// </summary>
    public string GroupSearchFilter { get; set; }

    /// <summary>
    /// The DN groups are searched under with <see cref="GroupSearchFilter"/>, or empty for
    /// <see cref="SearchBase"/>.
    /// </summary>
    public string GroupSearchBase { get; set; }

    /// <summary>
    /// The directory's name in the web UI: "Use your {DisplayName} or local account".
    /// </summary>
    public string DisplayName { get; set; } = "LDAP";

    /// <summary>
    /// How long, in seconds, a successful sign-in is remembered, so a client tool's requests don't
    /// each reach the directory. 0 checks every request against the directory.
    /// </summary>
    public int CacheSeconds { get; set; } = 60;

    /// <summary>
    /// How long, in seconds, to wait for the LDAP server.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;
}
