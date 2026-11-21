# SAML single sign-on

Besides [Microsoft Entra ID](authentication.md#azure-entra-id-setup) and [OpenID Connect](oidc.md), PaGetto signs users in through a SAML 2.0 identity provider: Keycloak, Okta, AD FS, Shibboleth, Entra ID enterprise applications, PingFederate and others. Users get a PaGetto account on their first sign-in, and the groups or roles the identity provider sends decide which PaGetto groups they are in.

Package clients don't sign in through the identity provider. A user signs in to the web UI once, creates a [personal access token](authentication.md#personal-access-tokens-pats) under **My Tokens**, and gives it to the client.

## Modes

| `Authentication:Mode` | Who signs in |
|---|---|
| `Saml` | Only users of the SAML identity provider. No local accounts, and no default administrator is created. |
| `Hybrid` | Users of the SAML identity provider and local accounts. In `Hybrid` mode the `Authentication:Saml` section replaces Entra ID: set only one of `Authentication:Entra`, `Authentication:Oidc` and `Authentication:Saml`. Local accounts can be joined by [LDAP](ldap.md) users. |

## Configure the identity provider

Register PaGetto at the identity provider as a service provider (also called a relying party or SAML application):

- **Entity id** (also called the audience or identifier): the `EntityId` you set in PaGetto, for example `https://packages.example.com`.
- **Assertion consumer service URL** (also called the reply or single sign-on URL): `https://your-pagetto-url/signin-saml` (the `CallbackPath`), with the HTTP-POST binding. Behind a reverse proxy, see [Trusted reverse proxies](configuration.md#trusted-reverse-proxies) so that PaGetto builds the URL with the public host and `https`.
- **Signing**: the identity provider has to sign the response or the assertion. PaGetto doesn't sign its requests, so turn off a requirement for signed requests. Encrypted assertions are not supported.
- **Name ID**: a stable id of the user, such as the username, the email address or a persistent id. PaGetto links the account to it, so a transient (one-time) name id would create a new account on every sign-in.
- **Attributes**: `email` with the user's email address, and optionally a group attribute, `groups` by default, with one value per group. A display name is read from a `name` attribute, or from the given name and surname claims that AD FS and Entra ID send.

Then note the URL of the identity provider's metadata. PaGetto reads the identity provider's single sign-on URL and signing certificates from it on the first sign-in after a start, and again every day, so a renewed certificate is picked up without a restart.

### Keycloak

1. In your realm (here `pagetto`), go to **Clients** > **Create client**. Set the **Client type** to **SAML** and the **Client ID** to the entity id, for example `https://packages.example.com`, and select **Next**.
2. Set **Valid redirect URIs** to `https://your-pagetto-url/signin-saml` and save.
3. On the client's **Settings** tab, set **Name ID format** to `username` and turn on **Force name ID format**. Keep **Sign documents** on.
4. On the **Keys** tab, turn off **Client signature required**.
5. On the **Client scopes** tab, open `<client id>-dedicated` and add two mappers with **Configure a new mapper** (or **Add mapper** > **By configuration**):
   - **User Property**: **Name** `email`, **Property** `email`, **SAML Attribute Name** `email`.
   - **Group list**: **Name** `groups`, **Group attribute name** `groups`, **Full group path** off.
6. Create the groups under **Groups**, for example `Admin` and `developers`, and add users to them.

The metadata URL is `https://keycloak.example.com/realms/pagetto/protocol/saml/descriptor`.

### Other identity providers

| Identity provider | Metadata URL | Group attribute |
|---|---|---|
| Okta | The app's **Sign On** tab > **Metadata URL** (`https://<your-org>.okta.com/app/<app-id>/sso/saml/metadata`) | Add a **Group Attribute Statement** named `groups` with a filter |
| Entra ID (enterprise application) | **Single sign-on** > **SAML Certificates** > **App Federation Metadata Url** | Add a group claim and set `RoleAttribute` to `http://schemas.microsoft.com/ws/2008/06/identity/claims/groups` (group object ids), or assign app roles and use `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` |
| AD FS | `https://adfs.example.com/FederationMetadata/2007-06/FederationMetadata.xml` | Add claim rules that issue a Name ID and send the groups as `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`, and set `RoleAttribute` to that name |

## Configure PaGetto

```json
{
    "Authentication": {
        "Mode": "Saml",
        "Saml": {
            "MetadataUrl": "https://keycloak.example.com/realms/pagetto/protocol/saml/descriptor",
            "EntityId": "https://packages.example.com",
            "DisplayName": "Keycloak"
        }
    }
}
```

| Setting | Required | Default | Description |
|---|---|---|---|
| `MetadataUrl` | Yes | | The URL of the identity provider's metadata. |
| `EntityId` | Yes | | PaGetto's entity id at the identity provider. Responses for another audience are refused. |
| `CallbackPath` | No | `/signin-saml` | The path of the assertion consumer service URL registered at the identity provider. |
| `RoleAttribute` | No | `groups` | The attribute whose values link the user to [role-linked groups](authentication.md#groups). The value `Admin` makes the user an administrator. |
| `DisplayName` | No | `SSO` | The identity provider's name on the sign-in page: **Sign in with Keycloak**. |
| `RequireHttpsMetadata` | No | `true` | Whether the metadata URL has to use `https`. The metadata holds the certificates responses are checked with, so set it to `false` only for an identity provider on a local test network. |

PaGetto checks these settings on startup and refuses to start when `MetadataUrl` or `EntityId` is missing, the metadata URL isn't an absolute URL (or uses `http` while `RequireHttpsMetadata` is on), `CallbackPath` doesn't start with `/` or `RoleAttribute` is empty. In `Hybrid` mode it also refuses more than one of the `Entra`, `Oidc` and `Saml` sections.

The same settings as environment variables:

```shell
Authentication__Mode=Saml
Authentication__Saml__MetadataUrl=https://keycloak.example.com/realms/pagetto/protocol/saml/descriptor
Authentication__Saml__EntityId=https://packages.example.com
Authentication__Saml__DisplayName=Keycloak
```

## What happens on sign-in

1. The sign-in page shows **Sign in with** and the `DisplayName`, which sends the user to the identity provider with an authentication request (HTTP-Redirect binding). The request's id waits in a short-lived cookie.
2. The identity provider posts its response to the `CallbackPath`. PaGetto checks its signature against the certificates in the metadata, its audience (`EntityId`), its validity period, and that it answers the request started in the same browser.
3. PaGetto finds the user's account by the name id. On the first sign-in it creates the account. Its username is the `email` attribute, otherwise the name id; both the username and the display name are updated on every sign-in.
4. **Admin**: the user is an administrator exactly while the `RoleAttribute` contains `Admin` (not case-sensitive). Administrator rights of a single sign-on account can't be changed in PaGetto.
5. **Groups**: the user joins every PaGetto group whose **App role value** is one of the `RoleAttribute` values and leaves the role-linked groups whose value is gone. Groups without an app role value are never touched.
6. PaGetto issues its [session cookie](authentication.md#sessions).

Sign-in is refused when an administrator has disabled the account or turned off its web sign-in, and when a new user's username is already used by another account (for example an identity provider user `admin` and the local `admin` in `Hybrid` mode). An existing account keeps its username when the new one is taken.

Signing out of PaGetto ends the PaGetto session only: PaGetto doesn't send a single logout request to the identity provider.

## Troubleshooting

### The sign-in page shows "authentication_failed"

The identity provider sent an error back, or the response was refused. The PaGetto log has the reason (`Single sign-on remote failure.` with the exception, or `Login denied: ...`). Frequent causes:

- The response or assertion isn't signed, or is signed with a certificate that isn't in the metadata.
- The audience of the assertion isn't the `EntityId`.
- The sign-in took longer than 15 minutes, or the browser didn't send the sign-in cookie back. The cookie is `Secure` and `SameSite=None`, as the identity provider posts from another site, so PaGetto has to be reached over `https` (browsers make an exception for `http://localhost`).

### PaGetto can't read the metadata

The first sign-in after a start reads the metadata; if that fails, the sign-in fails with an error page and the log has the exception. Check that PaGetto can reach the `MetadataUrl` and trusts its certificate. Once the metadata has been read, a failed daily refresh keeps the previous metadata and logs a warning.

### Groups are not synced

1. Look at a response with the identity provider's tools or a browser extension such as SAML-tracer, and check that it has the attribute named in `RoleAttribute`, with one value per group.
2. With Keycloak, check that **Full group path** is off; otherwise the values start with `/`.
3. Check that the PaGetto group's **App role value** matches the attribute value. The comparison is not case-sensitive.
