# LDAP and Active Directory

PaGetto checks usernames and passwords against an LDAP directory: OpenLDAP, Active Directory, FreeIPA, 389 Directory Server and others. Directory users sign in to the web UI with their directory username and password, and client tools use the same credentials with Basic authentication. Users get a PaGetto account on their first sign-in, and their directory groups decide which PaGetto groups they are in.

LDAP works next to local accounts, in the `Local` and `Hybrid` [modes](authentication.md#authentication-modes). In `Hybrid` mode users can also sign in through single sign-on (Entra ID, [OpenID Connect](oidc.md) or [SAML](saml.md)), so the same people can come in through a directory and an identity provider; they get one account per provider.

## How it works

On every sign-in that isn't remembered (see [`CacheSeconds`](#configure-pagetto)), PaGetto:

1. Connects to the server and binds with the search account (`BindDn`), or searches anonymously when no search account is set.
2. Searches `SearchBase` with `UserFilter`, in which `{0}` is the typed username (escaped, so it can't change the filter). Exactly one entry has to match.
3. Reads the user's groups: the DNs in its `memberOf` attribute, or the result of `GroupSearchFilter` for directories without `memberOf`.
4. Binds as the user with the typed password. A wrong password fails here.
5. Finds the PaGetto account by the entry's permanent id (`IdAttribute`: `entryUUID`, or `objectGUID` in Active Directory), so a renamed or moved user keeps its account. On the first sign-in it creates the account. The username is the value of `UsernameAttribute`, the display name `displayName` (or `cn`) and the email `mail`; they are updated on every sign-in.
6. Syncs the groups: the user is an administrator exactly while one of its groups is named `Admin`, and joins every PaGetto group whose **App role value** is the common name (`cn`) or the full DN of one of its directory groups. It leaves the role-linked groups it no longer belongs to. Groups without an app role value are never touched.

A username that belongs to a local account is only checked against that account, never against the directory. A new directory user whose username belongs to another account (a local account, or a single sign-on account with the same name) is refused, so a directory user `admin` can't take over the local `admin`.

## Configure PaGetto

OpenLDAP, with the `memberOf` overlay:

```json
{
    "Authentication": {
        "Mode": "Local",
        "Ldap": {
            "Server": "ldap.example.com",
            "Security": "Ssl",
            "BindDn": "cn=pagetto,ou=services,dc=example,dc=com",
            "BindPassword": "<password>",
            "SearchBase": "ou=people,dc=example,dc=com",
            "DisplayName": "Example directory"
        }
    }
}
```

Active Directory:

```json
{
    "Authentication": {
        "Mode": "Local",
        "Ldap": {
            "Server": "dc01.corp.example.com",
            "Security": "Ssl",
            "BindDn": "CN=svc-pagetto,OU=Service Accounts,DC=corp,DC=example,DC=com",
            "BindPassword": "<password>",
            "SearchBase": "OU=Users,DC=corp,DC=example,DC=com",
            "UserFilter": "(&(objectClass=user)(sAMAccountName={0}))",
            "UsernameAttribute": "sAMAccountName",
            "IdAttribute": "objectGUID",
            "DisplayName": "Corp account"
        }
    }
}
```

To let users sign in with their user principal name (`alice@corp.example.com`) as well, use `(&(objectClass=user)(|(sAMAccountName={0})(userPrincipalName={0})))`; their PaGetto username is still the `sAMAccountName`.

Keep the search account's password out of `appsettings.json` in production: set it as the environment variable `Authentication__Ldap__BindPassword` or as the secret file `/run/secrets/Authentication__Ldap__BindPassword` (see [Load secrets from files](configuration.md#load-secrets-from-files)). The search account only needs to read the users and groups.

| Setting | Required | Default | Description |
|---|---|---|---|
| `Server` | Yes | | The host name of the LDAP server. |
| `Port` | No | `0` | The port, or `0` for 636 with `Ssl` and 389 otherwise. |
| `Security` | No | `Ssl` | `Ssl` (LDAPS), `StartTls` (a plain connection upgraded before binding) or `None`. The server's certificate has to be trusted by the machine or container. With `None` passwords cross the network unencrypted: use it only on a test network. |
| `BindDn` | No | | The DN of the search account, or empty to search anonymously. |
| `BindPassword` | No | | The search account's password. |
| `SearchBase` | Yes | | The DN users are searched under. |
| `UserFilter` | No | `(&(objectClass=person)(uid={0}))` | The filter that finds a user; `{0}` is the typed username. |
| `UsernameAttribute` | No | `uid` | The attribute that holds the PaGetto username (`sAMAccountName` in Active Directory). |
| `IdAttribute` | No | `entryUUID` | The attribute with the user's permanent id (`objectGUID` in Active Directory, `nsUniqueId` in 389 Directory Server). |
| `DisplayNameAttribute` | No | `displayName` | The display name; `cn` is used without it. |
| `EmailAttribute` | No | `mail` | The email address, where PaGetto sends the user's notifications. |
| `GroupAttribute` | No | `memberOf` | The attribute of the user that lists the DNs of its groups. |
| `GroupSearchFilter` | No | | A filter that finds the user's groups instead of `GroupAttribute`, for directories without `memberOf`; `{0}` is the user's DN. For example `(&(objectClass=groupOfNames)(member={0}))`. |
| `GroupSearchBase` | No | `SearchBase` | Where `GroupSearchFilter` searches. |
| `DisplayName` | No | `LDAP` | The directory's name on the sign-in page: "Use your Corp account or local account to continue." |
| `CacheSeconds` | No | `60` | How long a successful sign-in is remembered, so a client tool's requests don't each reach the directory. `0` checks every request. A user disabled or removed in the directory can still sign in for this long. |
| `TimeoutSeconds` | No | `10` | How long to wait for the server. |

PaGetto checks these settings on startup and refuses to start when `Server` or `SearchBase` is missing, a filter lacks `{0}`, `UsernameAttribute` or `IdAttribute` is empty, a number is out of range, or the section is set in a mode without local accounts (`Entra`, `Oidc` or `Saml`).

The same settings as environment variables:

```shell
Authentication__Mode=Local
Authentication__Ldap__Server=ldap.example.com
Authentication__Ldap__BindDn=cn=pagetto,ou=services,dc=example,dc=com
Authentication__Ldap__BindPassword=<password>
Authentication__Ldap__SearchBase=ou=people,dc=example,dc=com
```

## Groups and permissions

Create the PaGetto groups on **Admin > Groups & permissions** with the directory group's common name (for example `developers`) or its full DN as their **App role value**, and give them their feed permissions there. The DN is useful when two directory groups share a common name. Members follow the directory on every sign-in and can't be added or removed by hand; local accounts can still be added to these groups manually.

A directory group named `Admin` makes its members PaGetto administrators. To keep administrator rights in PaGetto's hands, don't create a directory group with that name, or don't put directory users in it.

## Client tools

Directory users authenticate client tools with their directory username and password, or with a [personal access token](authentication.md#personal-access-tokens-pats) from **My Tokens** as the password, which keeps the directory password out of build configuration and works when the directory can't be reached. Token-based access is recommended for build agents.

## Troubleshooting

### Sign-in says "Invalid username or password."

The PaGetto log says why:

- `LDAP sign-in of ... failed: no such directory user or a wrong password`: the filter found no entry, or the password was wrong. Check `SearchBase` and `UserFilter`, for example with `ldapsearch -H ldaps://ldap.example.com -D "<BindDn>" -W -b "<SearchBase>" "(uid=alice)"`.
- `LDAP sign-in of ... failed: the directory could not be searched`, with the exception: the server can't be reached, its certificate isn't trusted, or the search account was refused.
- `more than one directory entry matches the username`: make `UserFilter` stricter.
- `the directory entry ... has no ... attribute`: the entry lacks `IdAttribute` or `UsernameAttribute`; Active Directory needs `objectGUID` and `sAMAccountName`.
- `Login denied: the username ... of the new Ldap user is already used by another account`: a local or single sign-on account has that username.

### The certificate of the server isn't trusted

PaGetto validates the LDAP server's certificate against the machine's trusted root certificates. For a certificate from an internal certificate authority, add the authority's certificate to the trust store of the machine, or of the container: mount it into `/usr/local/share/ca-certificates/` and run `update-ca-certificates`, or build an image on top of `letreset/pagetto` that does.

### Groups are not synced

1. Check that the user entry has `memberOf` (OpenLDAP needs the `memberof` overlay), or set `GroupSearchFilter`.
2. Check that the PaGetto group's **App role value** is the group's common name or full DN. The comparison is not case-sensitive.
3. Remember that a sign-in is remembered for `CacheSeconds`: group changes apply on the next sign-in after that.
