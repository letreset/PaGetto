# OpenID Connect single sign-on

Besides [Microsoft Entra ID](authentication.md#azure-entra-id-setup), PaGetto signs users in through any OpenID Connect provider: Keycloak, Okta, Auth0, Google Workspace, GitLab, Authentik, Zitadel and others. Users get a PaGetto account on their first sign-in, and the groups they belong to at the provider decide which PaGetto groups they are in, the way Entra ID app roles do.

Package clients don't sign in through the provider. A user signs in to the web UI once, creates a [personal access token](authentication.md#personal-access-tokens-pats) under **My Tokens**, and gives it to the client.

## Modes

| `Authentication:Mode` | Who signs in |
|---|---|
| `Oidc` | Only users of the OpenID Connect provider. No local accounts, and no default administrator is created. |
| `Hybrid` | Users of the OpenID Connect provider and local accounts. In `Hybrid` mode the `Authentication:Oidc` section replaces Entra ID: set either `Authentication:Entra` or `Authentication:Oidc`, not both. |

## Configure the provider

Register PaGetto at the provider as a web application (a confidential client) that uses the authorization code flow:

- **Redirect URI**: `https://your-pagetto-url/signin-oidc` (the `CallbackPath`). Behind a reverse proxy, see [Trusted reverse proxies](configuration.md#trusted-reverse-proxies) so that PaGetto builds the URI with the public host and `https`.
- **Scopes**: `openid`, `profile` and `email`.
- **Group claim** (optional): a claim in the **ID token** that lists the user's groups or roles, `groups` by default. PaGetto only reads the ID token, not the user info endpoint.

Then note the issuer URL (the authority), the client id and the client secret.

### Keycloak

1. In your realm (here `pagetto`), go to **Clients** > **Create client**. Keep **OpenID Connect**, set the **Client ID** to `pagetto` and select **Next**.
2. Turn on **Client authentication**, keep **Standard flow** and select **Next**.
3. Set **Valid redirect URIs** to `https://your-pagetto-url/signin-oidc` and save.
4. Copy the **Client secret** from the client's **Credentials** tab.
5. To sync groups, open the client's **Client scopes** tab > `pagetto-dedicated` > **Configure a new mapper** > **Group Membership**. Set **Name** and **Token Claim Name** to `groups`, turn **Full group path** off and keep **Add to ID token** on.
6. Create the groups under **Groups**, for example `Admin` and `developers`, and add users to them.

The authority is the realm's URL: `https://keycloak.example.com/realms/pagetto`.

Keycloak 24 and later ask a user without an email to complete their profile on the first sign-in (the **Verify Profile** required action). PaGetto doesn't need an email: without one, the username is the Keycloak username.

### Other providers

| Provider | Authority | Group claim |
|---|---|---|
| Okta | `https://<your-org>.okta.com` or a custom authorization server (`https://<your-org>.okta.com/oauth2/default`) | Add a **Groups claim** named `groups` with a filter to the app (org authorization server) or to the authorization server's ID token claims |
| Auth0 | `https://<tenant>.auth0.com/` | Auth0 adds no groups claim. Add one with a post-login Action, for example `https://pagetto/roles` from the user's roles, and set `RoleClaim` to that name |
| Google Workspace | `https://accounts.google.com` | Google sends no groups claim, so manage the members of every group in PaGetto |
| GitLab | `https://gitlab.com` (or your instance) | `groups_direct`, with the scope `openid profile email` |
| Authentik | `https://authentik.example.com/application/o/<slug>/` | `groups` (in the default `profile` scope) |

## Configure PaGetto

```json
{
    "Authentication": {
        "Mode": "Oidc",
        "Oidc": {
            "Authority": "https://keycloak.example.com/realms/pagetto",
            "ClientId": "pagetto",
            "ClientSecret": "<client-secret>",
            "DisplayName": "Keycloak"
        }
    }
}
```

Keep the client secret out of `appsettings.json` in production: set it as the environment variable `Authentication__Oidc__ClientSecret` or as the secret file `/run/secrets/Authentication__Oidc__ClientSecret` (see [Load secrets from files](configuration.md#load-secrets-from-files)).

| Setting | Required | Default | Description |
|---|---|---|---|
| `Authority` | Yes | | The provider's issuer URL. PaGetto reads `{Authority}/.well-known/openid-configuration`. |
| `ClientId` | Yes | | The client id registered at the provider. |
| `ClientSecret` | No | | The client secret, for a confidential client. |
| `Scopes` | No | `openid profile email` | The scopes to request, separated by spaces. Must include `openid`. |
| `CallbackPath` | No | `/signin-oidc` | The path of the redirect URI registered at the provider. |
| `RoleClaim` | No | `groups` | The ID token claim whose values link the user to [role-linked groups](authentication.md#groups). The value `Admin` makes the user an administrator. |
| `DisplayName` | No | `SSO` | The provider's name on the sign-in page: **Sign in with Keycloak**. |
| `RequireHttpsMetadata` | No | `true` | Whether the authority has to use `https`. Set it to `false` only for a provider on a local test network. |

PaGetto checks these settings on startup and refuses to start when `Authority` or `ClientId` is missing, the authority isn't an absolute URL (or uses `http` while `RequireHttpsMetadata` is on), `Scopes` lacks `openid`, `CallbackPath` doesn't start with `/` or `RoleClaim` is empty. In `Hybrid` mode it also refuses an `Entra` and an `Oidc` section together.

The same settings as environment variables:

```shell
Authentication__Mode=Oidc
Authentication__Oidc__Authority=https://keycloak.example.com/realms/pagetto
Authentication__Oidc__ClientId=pagetto
Authentication__Oidc__ClientSecret=<client-secret>
Authentication__Oidc__DisplayName=Keycloak
```

## What happens on sign-in

1. The sign-in page shows **Sign in with** and the `DisplayName`, which sends the user to the provider (authorization code flow with PKCE).
2. PaGetto validates the ID token and finds the user's account by the token's `sub` claim. On the first sign-in it creates the account. Its username is the `email` claim, otherwise `preferred_username`, otherwise the `sub`; its display name is the `name` claim. Both are updated on every sign-in.
3. **Admin**: the user is an administrator exactly while the `RoleClaim` contains `Admin` (not case-sensitive). Administrator rights of a single sign-on account can't be changed in PaGetto.
4. **Groups**: the user joins every PaGetto group whose **App role value** is one of the `RoleClaim` values and leaves the role-linked groups whose value is gone. Groups without an app role value are never touched. Create the groups on **Admin > Groups & permissions** with the provider's group names as their **App role value**, and give the groups their feed permissions there.
5. PaGetto issues its [session cookie](authentication.md#sessions).

Sign-in is refused when an administrator has disabled the account or turned off its web sign-in, and when a new user's username is already used by another account (for example a provider user `admin` and the local `admin` in `Hybrid` mode). An existing account keeps its username when the new one is taken.

Signing out of PaGetto ends the PaGetto session only, not the session at the provider.

## Troubleshooting

### The sign-in page shows "authentication_failed"

The provider sent an error back, or the sign-in was refused. The PaGetto log has the reason (`Single sign-on remote failure.` with the exception, or `Login denied: ...`). A frequent cause is a redirect URI at the provider that doesn't match `https://your-pagetto-url` plus `CallbackPath`.

### Groups are not synced

1. Decode an ID token (for example with the provider's token preview, or jwt.io) and check that it has the claim named in `RoleClaim`.
2. With Keycloak, check that the mapper adds the claim to the ID token and that **Full group path** is off; otherwise the values start with `/`.
3. Check that the PaGetto group's **App role value** matches the claim value. The comparison is not case-sensitive.
