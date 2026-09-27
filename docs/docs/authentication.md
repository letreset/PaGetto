# Authentication

PaGetto can protect your feeds with API keys from configuration, with local accounts stored in its database, with Microsoft Entra ID (formerly Azure AD) single sign-on, or with Entra ID and local accounts together. Database-backed modes add groups, per-feed permissions and personal access tokens.

## Authentication modes

The `Authentication:Mode` setting controls which mechanisms are active:

| Mode | Who signs in | Use it when |
|------|-------------|-------------|
| `Legacy` | Nobody. Pushes need an API key from configuration, reads optionally need a username and password from configuration. | Existing BaGetter-style setups. Formerly called `Config`, which is still accepted. See [Require an API key](configuration.md#require-an-api-key) and [Private feeds](configuration.md#private-feeds). |
| `Local` | Local accounts created by an administrator | You don't use Entra ID but want users, groups and per-feed permissions |
| `Entra` | Microsoft Entra ID accounts only | Everyone in your organization has an Entra ID account |
| `Hybrid` | Entra ID accounts and local accounts | People sign in with Entra ID, and build agents or external partners use local accounts |

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
- **`Local`** (and optionally `Hybrid`): on startup, while no user is an administrator, PaGetto creates the local account **`admin`** with the password **`admin`** and logs a warning. Sign in with it: PaGetto sends you straight to **Change password** and you can't open any other page until you have chosen a new password (at least 12 characters, or `Authentication:MinPasswordLength`). Until then the default password is also rejected by NuGet clients.

The default administrator is only ever created, never changed:

- Once an enabled administrator with web sign-in exists, nothing happens on startup.
- An existing user is never changed and a password is never reset. If a user named `admin` already exists (but no administrator can sign in), PaGetto logs a warning and leaves it alone.
- **Recovery:** if no administrator can sign in any more (every administrator is disabled or has lost web sign-in), rename or delete the `admin` user in the database and restart. PaGetto creates a new `admin` account with the default password.
- Several replicas can start at once: only one creates the account, the others skip it.

Change the default password right after the first start, before the server is reachable by others. Every local user can change their own password later under **Change password** in the account menu.

In `Legacy` and `Entra` mode no default administrator is created.

## Azure Entra ID setup

### Prerequisites

1. An Azure Entra ID tenant
2. An App Registration in your tenant
3. A client secret for the App Registration
4. (Optional) App Roles defined in the App Registration for role-based group sync

### Step 1: Create an App Registration

1. Go to the [Azure Portal](https://portal.azure.com) > **Microsoft Entra ID** > **App registrations** > **New registration**
2. Set the **Name** (e.g., "PaGetto NuGet Feed")
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

Signing in to the web UI, with a local account or with Entra ID, issues a session cookie (`PaGetto.Auth`) with a sliding expiration of `Authentication:SessionTimeoutMinutes` (default 60 minutes): every request extends it. The user is checked again on every request: disabling an account or its web sign-in signs the user out on their next request, and a change to their admin role applies without signing in again.

## Local accounts

When `Mode` is `Local` or `Hybrid`, administrators manage local accounts on **Admin > Accounts**. **New account** opens the create form; the other actions are in the **Actions** menu (⋮) at the end of each account's row:

- **Create** an account with a username (case-insensitive: `alice` and `ALICE` are the same account, on every database), an optional display name, an optional email address (used for [token expiry notifications](#expiry-notifications)) and a password of at least `Authentication:MinPasswordLength` characters (default 12, between 8 and 72). Passwords are stored as bcrypt hashes.
- **Enable account** or **Disable account**. Disabled accounts can't sign in or use their tokens.
- **Allow web sign-in** or **Disable web sign-in** (**Can sign in to web UI** when creating the account). Turn it off for build agents that should only use NuGet clients; the row then shows **API only** instead of **Web + API**.
- **Make admin** or **Remove admin role** for a local account. Entra accounts are administrators through the `Admin` app role instead. An **Admin** label marks administrators.
- **Unlock** an account that is [locked](#account-lockout) after too many failed sign-ins; a **Locked until** label shows when the lock ends.
- **Edit account**: change the username, display name and email in a dialog. After a rename the user signs in with the new username. Personal access tokens stay valid, but a `nuget.config` that sends the old username together with a token has to be updated.
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

- Wrong passwords count both on the sign-in page and from NuGet clients over Basic auth.
- While the account is locked, its password is refused everywhere, even when it is correct.
- A password that starts with `bg_` is only checked as a [personal access token](#personal-access-tokens-pats), so it never counts as a failed login. Tokens keep working while the account is locked.

## Groups

Groups are managed on **Admin > Groups & permissions**. A user inherits the permissions of every group they belong to. Group names are case-insensitive, like usernames. Groups come in two flavors:

- **Role-linked groups** have an `AppRoleValue` set (e.g., `TeamFrontend`), in the optional **App role value** field when you create the group. It can't be changed later. Membership for Entra users is synchronized from the token's `roles` claim on each sign-in and can't be changed by hand: it is controlled by the App Role assignments in Entra ID. Local users can still be added manually.
- **Manually-managed groups** have no `AppRoleValue`. Membership is managed entirely in the PaGetto admin UI, for all user types.

This lets Entra ID control *who has which role*, while PaGetto controls *what each role grants* on each feed.

## Feed permissions

In the `Local`, `Entra` and `Hybrid` modes every [feed](feeds.md) has its own permissions. They are granted to groups on **Admin > Groups & permissions**, one row per feed:

| Permission | Allows |
|------------|--------|
| Pull | Browsing the feed in the web UI, search, and restoring or downloading packages and symbols |
| Push | Publishing packages and symbol packages, and relisting through the NuGet API |
| Delete | Unlisting or deleting from NuGet clients (following the feed's [deletion behavior](feeds.md#feed-settings)), and unlisting, relisting and permanently deleting on the package page |

Administrators have all three on every feed. Feeds a user can't pull from are hidden from them in the UI.

- NuGet requests get `401 Unauthorized` when not signed in, and `403 Forbidden` when the user is signed in but lacks the needed permission.
- The package, Connect and Statistics pages and the Atom feed return `404 Not Found` to a signed-in user without pull permission, so they don't reveal what the feed contains.
- On a feed in [read-only mode](feeds.md#feed-settings), NuGet push, delete and relist requests with valid credentials get `403 Forbidden`, as the web UI does. Requests without valid credentials still get `401 Unauthorized`.

Feed slugs are not secret. NuGet clients need the service index (`/feeds/{slug}/v3/index.json`) without credentials to discover a feed, so an anonymous request can tell an existing feed (200) from a missing one (404). Pages and package data still need a signed-in user with pull permission, so don't put anything sensitive in a feed's slug.

## Personal access tokens (PATs)

Personal access tokens let users authenticate from NuGet clients and CI without their interactive credentials. They are available to Entra and local users.

- Users create tokens on **My Tokens** (in the user menu), with a name and an expiry of 30, 60, 90 (the default), 180 or 365 days.
- Local accounts that can't sign in to the web UI (for example build agents) get their tokens from an administrator: **New token** on **Admin > Accounts**.
- Users can only list and revoke their own tokens. There is no page to list or revoke another user's tokens; to cut off an account that only uses tokens, disable it.
- The token (it starts with `bg_`) is shown only once, at creation time.
- Tokens are stored as SHA-256 hashes, and can be revoked at any time.
- A token acts as its owner: it has exactly the owner's permissions, and stops working when the owner is disabled or deleted.
- A token created with **New token** on **Admin > Accounts** is written to the [audit log](configuration.md#audit-log) as `account_token_created`. Creating and revoking tokens on **My Tokens** is not: those are logged as `Audit: TokenCreated …` and `Audit: TokenRevoked …` lines at `Information` level, in the `PaGetto.Core.Authentication.TokenService` category, which the default `appsettings.json` doesn't log.

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

## Using PaGetto from NuGet clients

NuGet clients send a username and password (HTTP Basic) for restores, and an API key for pushes. What PaGetto accepts depends on the mode:

| Mode | Restore (username / password) | Push (`-k` API key) |
|---|---|---|
| `Legacy` | A `Credentials` entry, if any are configured | An `ApiKeys` value |
| `Local` | Your username and a PAT (recommended), or your account password | A PAT |
| `Entra` | Your username and a PAT as the password | A PAT |
| `Hybrid` | Your username and a PAT, or a local account's password | A PAT |

The **Connect** page of each feed shows the right instructions for the current mode.

Prefer a PAT over the account password on build agents and developer machines: it can expire, be revoked on its own, and failed PAT attempts never lock the account.

```shell
# Add the source (dotnet CLI)
dotnet nuget add source "https://your-pagetto-url/v3/index.json" \
    --name "PaGetto" \
    --username "<your-username>" \
    --password "<your-personal-access-token>"

# Push a package with a PAT
dotnet nuget push -s https://your-pagetto-url/v3/index.json \
    -k <your-personal-access-token> \
    package.1.0.0.nupkg
```

:::note

When a PAT is used as a password, the username must be the token owner's username; it is not case-sensitive. For an Entra user that is the email address PaGetto took from the token's `email`, `mail` or UPN claim, or the Entra object id when the token has none of them. Administrators see it on **Admin > Accounts**. Use the service index of the [feed](feeds.md#feed-urls) you want, e.g. `https://your-pagetto-url/feeds/internal/v3/index.json`.

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

The `Credentials` and `ApiKeys` arrays are only used when `Mode` is `Legacy`. When `Mode` is `Entra`, `Local`, or `Hybrid`, authentication is handled through the database-backed user system and PATs.

:::

## Environment variables

All authentication settings can be provided via environment variables using the double-underscore (`__`) separator:

| Environment Variable | Description |
|---------------------|-------------|
| `Authentication__Mode` | Authentication mode (required: `Local`, `Entra`, `Hybrid` or `Legacy`) |
| `Authentication__Entra__Instance` | Entra ID instance URL |
| `Authentication__Entra__TenantId` | Tenant ID |
| `Authentication__Entra__ClientId` | Application (client) ID |
| `Authentication__Entra__ClientSecret` | Client secret |
| `Authentication__Entra__CallbackPath` | OIDC callback path |
| `Authentication__Entra__RoleClaim` | Token claim name for App Roles (default: `roles`) |
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

### NuGet client returns 401 Unauthorized

1. Verify the NuGet source is configured with valid credentials for your mode (see [Using PaGetto from NuGet clients](#using-pagetto-from-nuget-clients))
2. Check that the user is in a group with `Pull` permission on the feed
3. If using a PAT, verify it has not expired or been revoked, and that the username matches the token owner
4. Ensure the `Mode` setting matches your authentication method (e.g., do not use `Credentials` when `Mode` is `Entra`)
