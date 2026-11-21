# Authentication

PaGetto can protect your feeds with API keys from configuration, with local accounts stored in its database, with single sign-on through Microsoft Entra ID (formerly Azure AD) or [another OpenID Connect provider](oidc.md), or with single sign-on and local accounts together. Database-backed modes add groups, per-feed permissions and personal access tokens.

## Authentication modes

The `Authentication:Mode` setting controls which mechanisms are active:

| Mode | Who signs in | Use it when |
|------|-------------|-------------|
| `Legacy` | Nobody. Pushes need an API key from configuration, reads optionally need a username and password from configuration. | Existing BaGetter-style setups. Formerly called `Config`, which is still accepted. See [Require an API key](configuration.md#require-an-api-key) and [Private feeds](configuration.md#private-feeds). |
| `Local` | Local accounts created by an administrator | You don't use Entra ID but want users, groups and per-feed permissions |
| `Entra` | Microsoft Entra ID accounts only | Everyone in your organization has an Entra ID account |
| `Oidc` | Accounts of an OpenID Connect provider only (Keycloak, Okta, Auth0, GitLab, ...) | Everyone signs in through your identity provider. See [OpenID Connect single sign-on](oidc.md). |
| `Hybrid` | Entra ID or OpenID Connect accounts, and local accounts | People sign in with single sign-on, and build agents or external partners use local accounts. The provider is the OpenID Connect provider when `Authentication:Oidc` is set, Entra ID otherwise. |

```json
{
    "Authentication": {
        "Mode": "Hybrid"
    }
}
```

:::info

`Authentication:Mode` is required: PaGetto refuses to start without it. When `Mode` is `Legacy` (formerly `Config`, still accepted), PaGetto uses `Authentication:ApiKeys` and `Authentication:Credentials` from configuration.

In every other mode those settings are ignored: there is no anonymous access, every request needs a signed-in user, a local account password or a personal access token, and what a user can do is decided by [feed permissions](#feed-permissions).

:::

## The first administrator

Administrators manage feeds, accounts, groups and permissions, and can pull, push and delete on every feed. How you get the first one depends on the mode:

- **`Entra` and `Hybrid`**: assign the `Admin` app role to yourself in Entra ID (see [Step 3](#step-3-define-app-roles-recommended)) and sign in. Admin status always follows the token.
- **`Oidc` and `Hybrid` with an OpenID Connect provider**: put yourself in the provider's `Admin` group (the value `Admin` in the [`RoleClaim`](oidc.md#configure-pagetto)) and sign in.
- **`Local`** (and optionally `Hybrid`): on startup, while no user is an administrator, PaGetto creates the local account **`admin`** with the password **`admin`** and logs a warning. Sign in with it: PaGetto sends you straight to **Change password** and you can't open any other page until you have chosen a new password (at least 12 characters, or `Authentication:MinPasswordLength`). Until then the default password is also rejected by package clients.

The default administrator is only ever created, never changed:

- Once an enabled administrator with web sign-in exists, nothing happens on startup.
- An existing user is never changed and a password is never reset. If a user named `admin` already exists (but no administrator can sign in), PaGetto logs a warning and leaves it alone.
- **Recovery:** if no administrator can sign in any more (every administrator is disabled or has lost web sign-in), rename or delete the `admin` user in the database and restart. PaGetto creates a new `admin` account with the default password.
- Several replicas can start at once: only one creates the account, the others skip it.

Change the default password right after the first start, before the server is reachable by others. Every local user can change their own password later under **Change password** in the account menu.

In `Legacy`, `Entra` and `Oidc` mode no default administrator is created.

## Azure Entra ID setup

### Prerequisites

1. An Azure Entra ID tenant
2. An App Registration in your tenant
3. A client secret for the App Registration
4. (Optional) App Roles defined in the App Registration for role-based group sync

### Step 1: Create an App Registration

1. Go to the [Azure Portal](https://portal.azure.com) > **Microsoft Entra ID** > **App registrations** > **New registration**
2. Set the **Name** (e.g., "PaGetto")
3. Set **Supported account types** to "Accounts in this organizational directory only" (single tenant)
4. Set the **Redirect URI** to `https://your-pagetto-url/signin-oidc` (type: Web)
5. Click **Register**
6. Note the **Application (client) ID** and **Directory (tenant) ID** from the overview page

### Step 2: Create a client secret

1. In your App Registration, go to **Certificates & secrets** > **Client secrets** > **New client secret**
2. Add a description and expiration period
3. Copy the secret **Value** immediately (it will not be shown again)

### Step 3: Define App Roles (recommended)

App Roles let you manage admin access and group memberships in Entra ID instead of in PaGetto. Roles appear in the `roles` claim of the ID token.

1. In your App Registration, go to **App roles** > **Create app role**
2. Create roles matching your team structure:

| Display Name | Value | Allowed Member Types | Description |
|---|---|---|---|
| Administrator | `Admin` | Users/Groups | Full admin access to PaGetto |
| Frontend Team | `TeamFrontend` | Users/Groups | Auto-joins "Frontend Team" group |
| Backend Team | `TeamBackend` | Users/Groups | Auto-joins "Backend Team" group |

3. In **Enterprise Applications** > your app > **Users and groups**, assign users or Entra security groups to the appropriate App Roles.

:::info

The `Admin` App Role is special and hardcoded. Any user whose token contains a role with value `Admin` is automatically granted `IsAdmin = true` and full access to all feeds. This cannot be overridden. All other role values work through group membership and feed permissions.

:::

### Step 4: Configure PaGetto

Add the Entra configuration to `appsettings.json`:

```json
{
    "Authentication": {
        "Mode": "Entra",
        "Entra": {
            "Instance": "https://login.microsoftonline.com/",
            "TenantId": "<your-tenant-id>",
            "ClientId": "<your-client-id>",
            "ClientSecret": "<your-client-secret>",
            "CallbackPath": "/signin-oidc",
            "RoleClaim": "roles"
        }
    }
}
```

:::warning

Do not store the `ClientSecret` in `appsettings.json` in production. Use environment variables, Docker secrets, or a secrets manager instead:

```shell
# Environment variable
Authentication__Entra__ClientSecret=your-client-secret

# Docker secret file
/run/secrets/Authentication__Entra__ClientSecret
```

:::

### Entra configuration reference

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `Instance` | Yes | | The Entra ID instance URL (e.g., `https://login.microsoftonline.com/`) |
| `TenantId` | Yes | | Your tenant ID |
| `ClientId` | Yes | | The application (client) ID from your App Registration |
| `ClientSecret` | Yes | | The client secret value |
| `CallbackPath` | No | `/signin-oidc` | The OIDC callback path. Must match the redirect URI in your App Registration. |
| `RoleClaim` | No | `roles` | The token claim name to read App Roles from. Change only if your identity provider uses a non-standard claim name. |

### How Entra authentication works

When a user signs in via Entra ID:

1. The user is redirected to Microsoft's login page
2. After successful authentication, the OIDC token is validated
3. PaGetto automatically provisions a local user record linked to the Entra Object ID, capturing the user's email from the token's `email`, `mail`, or UPN claim (used for [token expiry notifications](#expiry-notifications))
4. The `roles` claim is read from the token
5. **Admin sync (bidirectional):** If the token contains the `Admin` role, `IsAdmin` is set to `true`. If not, `IsAdmin` is set to `false`. Admin status is always driven by the token: there is no way to persist admin for an Entra user outside of the App Role.
6. **Group membership sync (full reconciliation):** The user is added to all PaGetto groups whose `AppRoleValue` matches a role in the token, and removed from role-linked groups whose role is no longer present. Manually-managed groups (no `AppRoleValue`) are never touched.
7. A [session cookie](#sessions) is issued

Sign-in is refused for an Entra account that an administrator has disabled or whose web sign-in is turned off.

## Sessions

Signing in to the web UI, with a local account or with single sign-on, issues a session cookie (`PaGetto.Auth`) with a sliding expiration of `Authentication:SessionTimeoutMinutes` (default 60 minutes): every request extends it. The user is checked again on every request: disabling an account or its web sign-in signs the user out on their next request, and a change to their admin role applies without signing in again.

## Local accounts

When `Mode` is `Local` or `Hybrid`, administrators manage local accounts on **Admin > Accounts**. **New account** opens the create form; the other actions are in the **Actions** menu (⋮) at the end of each account's row:

- **Create** an account with a username (case-insensitive: `alice` and `ALICE` are the same account, on every database), an optional display name, an optional email address (used for [token expiry notifications](#expiry-notifications)) and a password of at least `Authentication:MinPasswordLength` characters (default 12, between 8 and 72). Passwords are stored as bcrypt hashes.
- **Enable account** or **Disable account**. Disabled accounts can't sign in or use their tokens.
- **Allow web sign-in** or **Disable web sign-in** (**Can sign in to web UI** when creating the account). Turn it off for build agents that should only use package clients; the row then shows **API only** instead of **Web + API**.
- **Make admin** or **Remove admin role** for a local account. Entra accounts are administrators through the `Admin` app role instead. An **Admin** label marks administrators.
- **Unlock** an account that is [locked](#account-lockout) after too many failed sign-ins; a **Locked until** label shows when the lock ends.
- **Edit account**: change the username, display name and email in a dialog. After a rename the user signs in with the new username. Personal access tokens stay valid, but a client configuration (such as `nuget.config` or `~/.netrc`) that sends the old username together with a token has to be updated.
- **Reset password**: set a new password (at least `MinPasswordLength` characters) in a dialog. This also ends a [lockout](#account-lockout).
- **New token**: create a [personal access token](#personal-access-tokens-pats) for the account in a dialog.
- **Delete account**. The action only appears after the account has been disabled.

Entra users who have signed in are listed on the same page, marked **Entra**. For them only **Disable account** (or **Enable account**), **Disable web sign-in** (or **Allow web sign-in**) and, once disabled, **Delete account** are available; **Edit account**, **Reset password**, **New token** and **Make admin** are for local accounts only. A deleted Entra account is created again the next time that user signs in.

Your own row has no **Disable account**, **Disable web sign-in** or **Remove admin role** action, and PaGetto refuses to take those away from the last enabled administrator with web sign-in, so administration can't be locked out by accident.

Changes to accounts, groups and permissions on the **Admin** pages are written to the [audit log](configuration.md#audit-log).

### Account lockout

Local accounts are protected by an automatic lockout mechanism:

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxFailedAttempts` | `5` | Number of consecutive failed logins before lockout |
| `LockoutMinutes` | `15` | Duration (in minutes) that the account remains locked |

```json
{
    "Authentication": {
        "Mode": "Local",
        "MaxFailedAttempts": 5,
        "LockoutMinutes": 15
    }
}
```

After `MaxFailedAttempts` consecutive failed logins, the account is locked for `LockoutMinutes`. The counter resets on successful login.

- Wrong passwords count both on the sign-in page and from clients over Basic auth.
- While the account is locked, its password is refused everywhere, even when it is correct.
- A password that starts with `bg_` is only checked as a [personal access token](#personal-access-tokens-pats), so it never counts as a failed login. Tokens keep working while the account is locked.

## Groups

Groups are managed on **Admin > Groups & permissions**. A user inherits the permissions of every group they belong to. Group names are case-insensitive, like usernames. Groups come in two flavors:

- **Role-linked groups** have an `AppRoleValue` set (e.g., `TeamFrontend`), in the optional **App role value** field when you create the group. It can't be changed later. Membership for single sign-on users is synchronized from the token's role claim on each sign-in and can't be changed by hand: it is controlled by the App Role assignments in Entra ID, or by the groups at the [OpenID Connect provider](oidc.md#what-happens-on-sign-in). Local users can still be added manually.
- **Manually-managed groups** have no `AppRoleValue`. Membership is managed entirely in the PaGetto admin UI, for all user types.

This lets the identity provider control *who has which role*, while PaGetto controls *what each role grants* on each feed.

## Feed permissions

In the `Local`, `Entra` and `Hybrid` modes every [feed](feeds.md) has its own permissions. They are granted to groups on **Admin > Groups & permissions**, one row per feed:

| Permission | Allows |
|------------|--------|
| Pull | Browsing the feed in the web UI, search, and restoring or downloading packages and symbols |
| Push | Publishing packages (and NuGet symbol packages), and on NuGet feeds relisting through the NuGet API |
| Delete | Unlisting (yanking, retracting, retiring) or deleting from clients (on NuGet feeds following the feed's [deletion behavior](feeds.md#feed-settings)), and unlisting, relisting and permanently deleting on the package page |

Administrators have all three on every feed. Feeds a user can't pull from are hidden from them in the UI.

- Client requests get `401 Unauthorized` when not signed in. A signed-in user without pull permission gets `404 Not Found` on reads, the same as for a feed that doesn't exist. On push, delete and relist, a signed-in user without the permission gets `403 Forbidden` when they have some other permission on the feed, and `404 Not Found` when they have none.
- The feed's package list, package, Connect and Statistics pages and the Atom feed return `404 Not Found` to a signed-in user without pull permission, so they don't reveal that the feed exists or what it contains.
- On a feed in [read-only mode](feeds.md#feed-settings), publish, unlist, delete and relist requests from clients with valid credentials get `403 Forbidden` (or `404 Not Found` for a user with no permission on the feed), as the web UI does. Requests without valid credentials still get `401 Unauthorized`.

An anonymous request to `/feeds/{slug}/…` gets the same answer whether or not the feed exists: the service index, a `401` challenge on the other client endpoints, and the "Sign in required" prompt in the web UI. Once signed in, a feed the user has no permission on and a missing feed both return `404`.

## Personal access tokens (PATs)

Personal access tokens let users authenticate from package clients and CI without their interactive credentials. They are available to Entra and local users.

- Users create tokens on **My Tokens** (in the user menu), with a name, an expiry of 30, 60, 90 (the default), 180 or 365 days, an **Access** (**Read only**, **Read and publish**, or **Read, publish and delete**, the default) and the **Feeds** it works with (**All feeds**, the default, or one feed the user can read).
- Local accounts that can't sign in to the web UI (for example build agents) get their tokens from an administrator: **New token** on **Admin > Accounts**.
- Users can only list and revoke their own tokens on **My Tokens**. Administrators list any user's tokens with **Tokens** in the account's **Actions** menu on **Admin > Accounts**, and revoke one of them or all active ones there. A revoked token is rejected on the next request.
- The token (it starts with `bg_`) is shown only once, at creation time.
- Tokens are stored as SHA-256 hashes, and can be revoked at any time.
- A token acts as its owner, within its access and feeds: a read-only token of a user who can publish can only read, and a token of one feed can't read any other feed. It never has more than its owner's permissions, and stops working when the owner is disabled or deleted. Tokens created before tokens could be limited, and those from **New token** on **Admin > Accounts**, have all of their owner's permissions in every feed. A token never gives access to the web UI or the administration pages, which need a signed-in session.
- A token created with **New token** on **Admin > Accounts** is written to the [audit log](configuration.md#audit-log) as `account_token_created`, and tokens an administrator revokes as `token_revoked` or `tokens_revoked_all`. Creating and revoking tokens on **My Tokens** is not: those are logged as `Audit: TokenCreated …` and `Audit: TokenRevoked …` lines at `Information` level, in the `PaGetto.Core.Authentication.TokenService` category, which the default `appsettings.json` doesn't log.

### Token expiry

The maximum allowed token lifetime is controlled by the `MaxTokenExpiryDays` setting:

```json
{
    "Authentication": {
        "MaxTokenExpiryDays": 365
    }
}
```

The default is `365`. An expiry choice above the maximum is lowered to it without a message, and on **My Tokens** the 365-day choice is hidden while the maximum is lower. Tokens from **My Tokens** can't last longer than 365 days even when `MaxTokenExpiryDays` is higher.

### Expiry notifications

PaGetto can email token owners before their personal access tokens expire, so they can create a replacement before clients start failing authentication. This requires:

- [Email](configuration.md#email) to be configured (`Email:Type` set to `Smtp` or `Graph`). When email is disabled, the scanner does not run.
- The token owner to have a stored email address. Local users get one from the admin **Accounts** page; Entra users have it derived from their token's `email`, `mail`, or UPN claim on sign-in. Owners without an email address are skipped (with a warning logged).

A background scanner wakes every `ScanIntervalHours` and emails owners as each configured threshold (whole days before expiry) is crossed. Each threshold is sent at most once per token.

```json
{
    "Email": {
        "Type": "Smtp"
        // ... see the Email configuration section
    },
    "PatExpiryNotification": {
        "Enabled": true,
        "ScanIntervalHours": 1,
        "NotificationDaysBeforeExpiry": [ 14, 7, 2, 0 ]
    },
    "PublicBaseUrl": "https://packages.example.com"
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Whether the scanner runs. When `false`, no scanning or emailing happens regardless of email configuration. |
| `ScanIntervalHours` | `1` | How often (in hours) the scanner looks for tokens nearing expiry. Minimum `1`. |
| `NotificationDaysBeforeExpiry` | `[14, 7, 2, 0]` | Thresholds, in whole days before expiry, at which an owner is emailed. `0` means the expiry day itself. Values must be distinct and zero or greater. |
| `PublicBaseUrl` (top level) | | Public base URL of this site (e.g. `https://packages.example.com`), used to link owners to the token page. Must be an absolute `http(s)` URL when set. Omit for a name-only reference. The older `PatExpiryNotification:WebBaseUrl` is still read when it isn't set. |

:::info

The scanner runs outside an HTTP request and cannot infer the site URL, so `PublicBaseUrl` must be configured for notification emails to include a working link.

:::

## Using PaGetto from package clients

Every client authenticates with a [personal access token](#personal-access-tokens-pats), or in the `Legacy` mode with the [API keys](configuration.md#require-an-api-key) and [credentials](configuration.md#private-feeds) from configuration. Prefer a token over the account password on build agents and developer machines: it can expire, be revoked on its own, be limited to reading or to one feed, and failed token attempts never lock the account.

How the token reaches PaGetto depends on the client. The **Connect** page of each feed shows the right instructions for the feed's type and the current mode, and so does the page of each type:

| Clients | How they send the token | Setup |
|---|---|---|
| dotnet CLI, NuGet CLI, Visual Studio, Rider, Paket | Username and token with Basic authentication for restores, the token as the API key (`-k`) for pushes | [NuGet packages](nuget.md#authentication) |
| `dart pub`, `flutter pub` | Bearer token, stored with `dart pub token add <feed URL>` | [Dart and Flutter packages](dart.md#sign-in) |
| npm, Yarn, pnpm | Bearer token, the `_authToken` of the feed's registry URL in `.npmrc` (`.yarnrc.yml` for Yarn 2 and later); `npm login` is not supported | [npm packages](npm.md#configure-the-client) |
| Maven, Gradle, sbt | Username and token with Basic authentication: the `server` of the repository's id in `settings.xml`, the repository's `credentials` in Gradle | [Maven packages](maven.md#configure-the-client) |
| pip, uv, Poetry, twine | `__token__` (or the owner's username) and token with Basic authentication: `~/.netrc` for pip, environment variables for uv, `poetry config http-basic`, `~/.pypirc` for twine | [Python packages](pypi.md#configure-the-client) |
| Cargo | The token as the whole `Authorization` header, from `cargo login --registry <name>` or `CARGO_REGISTRIES_<NAME>_TOKEN`; a Rust feed asks Cargo to send it with every request | [Rust crates](cargo.md#configure-cargo) |
| go command | `__token__` (or the owner's username) and token from `~/.netrc` (`%USERPROFILE%\_netrc` on Windows), with Basic authentication and only over HTTPS | [Go modules](go.md#authentication) |
| Bundler, `gem` | Basic credentials from `bundle config set --global <host> __token__:<token>` or the source URL for installs; the token as the whole `Authorization` header (`GEM_HOST_API_KEY` or `~/.gem/credentials`) for `gem push` and `gem yank` | [Ruby gems](rubygems.md#authentication) |
| Mix, Rebar3 | The token as the whole `Authorization` header: `--auth-key` of `mix hex.repo add`, `repo_key` in `rebar.config`, `HEX_API_KEY` for `mix hex.publish` and `mix hex.retire` | [Hex packages](hex.md#authentication) |
| Composer | `__token__` (or the owner's username) and token as the `http-basic` credentials of `auth.json` | [PHP packages](composer.md#authentication) |
| CocoaPods | `__token__` (or the owner's username) and token from `~/.netrc`, with Basic authentication | [CocoaPods pods](cocoapods.md#authentication) |
| Swift Package Manager, Xcode | Bearer token, checked and stored by `swift package-registry login <feed URL>/login --token <token>`; `login` also takes a username and password for Basic authentication | [Swift packages](swift.md#authentication) |
| R (`install.packages`, renv) | `__token__` (or the owner's username) and token in the repository URL: `https://__token__:<token>@your-server/feeds/{slug}/cran` | [R packages](cran.md#authentication) |
| sbt with the Ivy layout | The credentials of the realm `PaGetto` and the server's host, with the token as the password | [Ivy repositories for sbt](ivy.md#authentication) |
| Visual Studio extension gallery | Visual Studio only sends Windows credentials, so it reads `https://your-server/feeds/{slug}/vsix/auth/<token>/atom.xml`, a URL with the token in its path that can only read; uploads use Basic authentication with the token as the password | [Visual Studio extensions](vsix.md#authentication) |
| VS Code-based editors (VSCodium, code-server) | The editors send no credentials to a gallery, so `product.json` uses `https://your-server/feeds/{slug}/vscode/auth/<token>/gallery`, a URL with the token in its path that can only read and query; uploads use Basic authentication with the token as the password | [VS Code extensions](vscode.md#authentication) |

:::note

When a token is used as a password, the username must be the token owner's username; it is not case-sensitive. The username `__token__` also goes with any token, as Python clients send PyPI's API tokens. For an Entra user the username is the email address PaGetto took from the token's `email`, `mail` or UPN claim, or the Entra object id when the token has none of them. For an [OpenID Connect](oidc.md) user it is the `email` claim, otherwise `preferred_username`. Administrators see it on **Admin > Accounts**.

:::

## Full configuration reference

```json
{
    "Authentication": {
        "Mode": "Hybrid",
        "Entra": {
            "Instance": "https://login.microsoftonline.com/",
            "TenantId": "<tenant-id>",
            "ClientId": "<client-id>",
            "ClientSecret": "<client-secret>",
            "CallbackPath": "/signin-oidc",
            "RoleClaim": "roles"
        },
        // Or, instead of "Entra", an OpenID Connect provider (see oidc.md):
        // "Oidc": {
        //     "Authority": "https://keycloak.example.com/realms/pagetto",
        //     "ClientId": "pagetto",
        //     "ClientSecret": "<client-secret>",
        //     "Scopes": "openid profile email",
        //     "CallbackPath": "/signin-oidc",
        //     "RoleClaim": "groups",
        //     "DisplayName": "Keycloak",
        //     "RequireHttpsMetadata": true
        // },
        "MaxTokenExpiryDays": 365,
        "MaxFailedAttempts": 5,
        "LockoutMinutes": 15,
        "SessionTimeoutMinutes": 60,
        "MinPasswordLength": 12,
        "Credentials": [
            {
                "Username": "legacy-user",
                "Password": "legacy-password"
            }
        ],
        "ApiKeys": [
            {
                "Key": "legacy-api-key"
            }
        ]
    }
}
```

:::info

The `Credentials` and `ApiKeys` arrays are only used when `Mode` is `Legacy`. When `Mode` is `Entra`, `Oidc`, `Local`, or `Hybrid`, authentication is handled through the database-backed user system and PATs.

:::

## Environment variables

All authentication settings can be provided via environment variables using the double-underscore (`__`) separator:

| Environment Variable | Description |
|---------------------|-------------|
| `Authentication__Mode` | Authentication mode (required: `Local`, `Entra`, `Oidc`, `Hybrid` or `Legacy`) |
| `Authentication__Entra__Instance` | Entra ID instance URL |
| `Authentication__Entra__TenantId` | Tenant ID |
| `Authentication__Entra__ClientId` | Application (client) ID |
| `Authentication__Entra__ClientSecret` | Client secret |
| `Authentication__Entra__CallbackPath` | OIDC callback path |
| `Authentication__Entra__RoleClaim` | Token claim name for App Roles (default: `roles`) |
| `Authentication__Oidc__Authority` | OpenID Connect issuer URL (see [OpenID Connect single sign-on](oidc.md#configure-pagetto) for every `Authentication__Oidc__*` setting) |
| `Authentication__MaxTokenExpiryDays` | Maximum PAT lifetime in days |
| `Authentication__MaxFailedAttempts` | Failed login threshold for lockout |
| `Authentication__LockoutMinutes` | Lockout duration in minutes |
| `Authentication__SessionTimeoutMinutes` | Web sign-in lifetime without activity, in minutes (default 60) |
| `Authentication__MinPasswordLength` | Minimum local account password length (default 12, 8 to 72) |

## Docker Compose example

```yaml
services:
  pagetto:
    image: letreset/pagetto:latest
    ports:
      - "5000:8080"
    environment:
      - Database__Type=PostgreSql
      - Database__ConnectionString=Host=db;Database=pagetto;Username=pagetto;Password=secret
      - Authentication__Mode=Entra
      - Authentication__Entra__Instance=https://login.microsoftonline.com/
      - Authentication__Entra__TenantId=your-tenant-id
      - Authentication__Entra__ClientId=your-client-id
      - Authentication__Entra__CallbackPath=/signin-oidc
      - Authentication__Entra__RoleClaim=roles
    volumes:
      - pagetto-data:/data
    secrets:
      - source: entra_client_secret
        target: Authentication__Entra__ClientSecret

volumes:
  pagetto-data:

secrets:
  entra_client_secret:
    file: ./secrets/entra-client-secret.txt
```

The secret is mounted at `/run/secrets/Authentication__Entra__ClientSecret`, which PaGetto reads as the `Authentication:Entra:ClientSecret` setting (see [Load secrets from files](configuration.md#load-secrets-from-files)).

## Database migrations

The authentication tables (Users, Groups, UserGroups, PersonalAccessTokens, FeedPermissions) are created automatically on startup via EF Core migrations. No manual migration steps are needed. See [Upgrading](upgrading.md).

## Troubleshooting

### "The 'TenantId' config is required for Entra authentication"

The `Authentication.Entra.TenantId` is missing or empty. Ensure your configuration or environment variables include the tenant ID from your Azure App Registration.

### "The 'ClientId' config is required for Entra authentication"

The `Authentication.Entra.ClientId` is missing or empty. Copy the Application (client) ID from the Azure Portal App Registration overview page.

### OIDC callback fails with "correlation failed"

This typically means the redirect URI in your Azure App Registration does not match the `CallbackPath` combined with your application's external URL. Verify:

1. The redirect URI in Azure is set to `https://your-external-url/signin-oidc`
2. Your reverse proxy (if any) is forwarding the `Host` header and the `X-Forwarded-Proto` header correctly
3. The application is using HTTPS in production

### Everyone is signed out after a restart

The Data Protection keys that protect the sign-in cookie are stored in package storage (`dataprotection/keyring.xml`). Make sure the storage (for Docker, the `/data` volume) is persistent and shared by all replicas.

### App Roles are missing from the token

If users are not getting admin permissions or group memberships despite being assigned App Roles:

1. Verify that **App Roles** are defined in the App Registration under **App roles**
2. Verify users are assigned to the roles in **Enterprise Applications** > your app > **Users and groups**
3. Check that the token includes the `roles` claim (use [jwt.ms](https://jwt.ms) to decode a token)
4. Ensure the `RoleClaim` config matches the claim name in your token (default: `roles`)
5. Verify that PaGetto groups have the correct `AppRoleValue` set to match the role values in the token

### Admin role not granting access

The admin role value is hardcoded as `Admin` (not case-sensitive, so `admin` works too). Check the App Role's **Value** in Entra ID, not only its display name: the display name can be anything, but the value must be `Admin`.

### Local account is locked out

If a local account is locked after too many failed attempts, wait for the lockout period to expire (`LockoutMinutes`, default 15 minutes), or have an administrator reset the password on **Admin > Accounts**, which ends the lockout. Personal access tokens keep working while the account is locked.

### A client returns 401 Unauthorized

1. Verify the client is configured with valid credentials for your mode (see [Using PaGetto from package clients](#using-pagetto-from-package-clients))
2. Check that the user is in a group with `Pull` permission on the feed
3. If using a PAT, verify it has not expired or been revoked, that the username matches the token owner (or is `__token__`), and that the token's access and feeds cover the request
4. Ensure the `Mode` setting matches your authentication method (e.g., do not use `Credentials` when `Mode` is `Entra`)
