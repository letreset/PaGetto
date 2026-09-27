
# Configuration

You can modify PaGetto's configurations by editing the `appsettings.json` file. The shipped file only turns on what a SQLite install with local accounts needs (`Database`, `Storage`, `Search` and `Authentication:Mode`); every other setting is listed in it as a comment with its default value and a short description.

## Machine-wide config file

PaGetto also reads an optional `appsettings.json` from a fixed location outside the app folder, so IIS, Windows service and systemd installs can keep their settings when the app files are replaced:

- Windows, where `%ProgramData%` is usually `C:\ProgramData`:

  ```text
  %ProgramData%\PaGetto\appsettings.json
  ```

- Linux and macOS:

  ```text
  /etc/pagetto/appsettings.json
  ```

The file uses the same format as `appsettings.json`. It is reloaded when it changes, as long as its folder existed when PaGetto started; if you create the folder later, restart PaGetto once. Later sources override earlier ones:

1. `appsettings.json` and `appsettings.{Environment}.json` in the app folder (or in `PAGETTO_CONFIG_ROOT`, if set)
2. User secrets (Development only)
3. The machine-wide config file
4. Environment variables
5. Command line arguments
6. [Secrets from files](#load-secrets-from-files) under `/run/secrets`

Make sure the account PaGetto runs as can read the file.

## Require an API key

:::note

`ApiKeys` and `Credentials` (see [Private feeds](#private-feeds)) only apply when `Authentication:Mode` is `Legacy` (formerly `Config`). In the `Local`, `Entra` and `Hybrid` modes they are ignored, and clients push and restore with a personal access token or their credentials instead. See [Authentication](authentication.md).

:::

You can require that users provide a password, called an API key, to publish packages. Put one or more keys, for example one per team, in the `Authentication:ApiKeys` array:

```json
{
    "Authentication": {
        "Mode": "Legacy",
        "ApiKeys": [
            {
                "Key" : "NUGET-SERVER-API-KEY-1"
            },
            {
                "Key" : "NUGET-SERVER-API-KEY-2"
            }
        ]
        ...
    }
    ...
}
```

Any of the keys is accepted. The top-level `ApiKey` setting of older versions and of BaGetter was removed: PaGetto refuses to start while it is set, so move the key into `ApiKeys` (as the environment variable `Authentication__ApiKeys__0__Key`).

Users will now have to provide the API key to push packages:

```shell
dotnet nuget push -s http://localhost:5000/v3/index.json -k NUGET-SERVER-API-KEY package.1.0.0.nupkg
```

## Hosting on a different path

By default, PaGetto is hosted at the root path `/` (e.g. `pagetto.your-company.org`). You can host PaGetto at a different path (e.g. `pagetto.your-company.org/pagetto`) by setting the `PathBase` field:

```json
{
    ...
    "PathBase": "/pagetto",
    ...
}
```

## Trusted reverse proxies

PaGetto reads the `X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host` headers, so it sees the client's address and the public scheme and host behind a reverse proxy. By default it accepts them from every client. When PaGetto is reachable directly as well as through the proxy, list the proxies so nobody else can fake these headers:

```json
{
    "ForwardedHeaders": {
        "KnownProxies": [ "10.0.0.5" ],
        "KnownNetworks": [ "10.1.0.0/16" ]
    }
}
```

Once either list is set, only those addresses are trusted. Startup fails if an entry isn't an IP address or a network in CIDR notation.

## Enable read-through caching

Read-through caching lets you index packages from an upstream source. You can use read-through
caching to:

1. Speed up your builds if restores from [nuget.org](https://nuget.org) are slow
2. Enable package restores in offline scenarios

Mirrors are configured per [feed](feeds.md) on **Admin > Feeds > Settings**, not in configuration: a feed can mirror one or more upstream sources (nuget.org, another v3 or v2 feed), with optional upstream authentication and a download timeout. See [Mirror (read-through cache)](feeds.md#mirror-read-through-cache).

The global `Mirror` section of older versions and of BaGetter is no longer read. PaGetto logs a warning at startup while it is present; add the mirror to the feed instead and remove the section.

## Enable package hard deletions

To prevent the ["left pad" problem](https://blog.npmjs.org/post/141577284765/kik-left-pad-and-npm),
PaGetto's default configuration doesn't allow package deletions. Whenever PaGetto receives a package deletion
request, it will instead "unlist" the package. An unlisted package is undiscoverable but can still be
downloaded if you know the package's id and version. You can override this behavior by setting the
`PackageDeletionBehavior`:

```json
{
    ...

    "PackageDeletionBehavior": "HardDelete",

    ...
}
```

## Package auto-deletion

If your build server generates many nuget packages, your PaGetto server can quickly run out of space. PaGetto leverages [SemVer 2](https://semver.org/) and has logic to keep a history of packages based on the version numbering such as `<major>.<minor>.<patch>-<prerelease tag>.<prerelease build number>`.

There is an optional config section for `Retention` and the following parameters can be enabled to limit history for each level of the version. If none of these are set, there are no cleaning rules enforced. Each parameter is optional, e.g. if you specify only a `MaxPatchVersions`, the package limit will only enforced within each major and minor version combination.
Packages deleted are always the oldest based on version numbers. The version that is currently being pushed or mirrored is never deleted by its own retention run, even if it falls outside the limits (for example, mirroring `1.0.0` when `3.0.0` is cached and `MaxMajorVersions` is `1`). It will be cleaned up the next time a version of that package is indexed.

- MaxMajorVersions: Maximum number of major versions for each package
- MaxMinorVersions: Maximum number of minor versions for each major version
- MaxPatchVersions: Maximum number of patch versions for each major + minor version
- MaxPrereleaseVersions: Maximum number of prerelease builds for each major + minor + patch version and prerelease type. If you have `beta` and `alpha` this will keep `MaxPrereleaseVersions` versions for both `beta` and `alpha`. Suffixes incompatible with [SemVer 2](https://semver.org/) will be treated as a separate type.

```json
{
    ...
    "Retention": {
        "MaxMajorVersions": 5,
        "MaxMinorVersions": 5,
        "MaxPatchVersions": 5,
        "MaxPrereleaseVersions": 5,
    }
    ...
}
```

`MaxVersionsPerPackage`, a retention setting of older versions, is no longer read; use the `Retention` settings instead.

## Enable package overwrites

Normally, PaGetto will reject a package upload if the id and version are already taken. This is to maintain the [immutability of semantically versioned packages](https://learn.microsoft.com/azure/devops/artifacts/artifacts-key-concepts?view=azure-devops#immutability).

:::warning

NuGet clients cache packages on multiple levels, so overwriting a package can lead to unexpected behavior.
A client may have a cached version of the package that is different from the one on the server.
Make sure that everyone involved is aware of the implications of overwriting packages.

:::

You can configure PaGetto to overwrite the already existing package by setting `AllowPackageOverwrites`:

```json
{
    ...

    "AllowPackageOverwrites": "true",

    ...
}
```

To allow pre-release versions to be overwritten but not stable releases, set `AllowPackageOverwrites` to `PrereleaseOnly`.

Pushing a package with a pre-release version like "3.1.0-SNAPSHOT" will overwrite the existing "3.1.0-SNAPSHOT" package, but pushing a "3.1.0" package will fail if a "3.1.0" package already exists.

## Read-only mode

Set `IsReadOnlyMode` to `true` to reject pushes, deletes, unlists and relists. Restores keep working. The default is `false`.

```json
{
    ...

    "IsReadOnlyMode": true,

    ...
}
```

Like `AllowPackageOverwrites` and `PackageDeletionBehavior`, this is the default for feeds that don't set their own value. See [Feed settings](feeds.md#feed-settings).

## Private feeds

A private feed requires users to authenticate before accessing packages.

The `Credentials` list below only applies in the `Legacy` authentication mode. With user accounts (`Local`, `Entra` or `Hybrid`), access is controlled per feed through groups and permissions instead; see [Authentication](authentication.md).

You can require that users provide a username and password to access the nuget feed.
To do so, you can insert the credentials in the `Authentication` section.

```json
{
    "Authentication": {
        "Credentials": [
            {
                "Username": "username",
                "Password": "password"
            }
        ]
        ...
    }
    ...
}
```

Users will now have to provide the username and password to fetch and download packages.

How to add private nuget feed:

1. Download the latest NuGet executable.
2. Open a Command Prompt and change the path to the nuget.exe location.
3. The command from the example below stores a token in the %AppData%\NuGet\NuGet.config file. Your original credentials cannot be obtained from this token.


```shell
NuGet Sources Add -Name "localhost" -Source "http://localhost:5000/v3/index.json" -UserName "username" -Password "password"
```

If you are unable to connect to the feed by using encrypted credentials, store your credentials in clear text:

```shell
NuGet Sources Add -Name "localhost" -Source "http://localhost:5000/v3/index.json" -UserName "username" -Password "password" -StorePasswordInClearText
```

If you have already stored a token instead of storing the credentials as clear text, update the definition in the %AppData%\NuGet\NuGet.config file by using the following command:

```shell
NuGet Sources Update -Name "localhost" -Source "http://localhost:5000/v3/index.json" -UserName "username" -Password "password" -StorePasswordInClearText
```

With the dotnet CLI:

```shell
dotnet nuget add source "http://localhost:5000/v3/index.json" --name "pagetto" --username "username" --password "password"
```

## Database configuration

PaGetto supports multiple database engines for storing package information:

- MySQL: `MySql`
- SQLite: `Sqlite`
- SQL Server: `SqlServer`
- PostgreSQL: `PostgreSql`

Each database engine requires a connection string to configure the connection. Please refer to [ConnectionStrings.com](https://www.connectionstrings.com/) to learn how to create the proper connection string for each database engine.

You may configure the chosen database engine either using environment variables or by editing the `appsettings.json` file.

:::info

Database migrations are applied automatically on application startup. To skip them, for example because you apply migrations with your own tooling, set `RunMigrationsAtStartup` to `false` (default `true`). PaGetto then expects the database schema to be up to date.

:::

### Environment Variables

These environment variables are related to database configuration:

- **Database__Type**: The database engine to use, this should be one of the strings from the above list such as `PostgreSql` or `Sqlite`.
- **Database__ConnectionString**: The connection string for your database engine.
- **Database__ServerVersion**: MySQL only, optional. The version of your MySQL or MariaDB server, see [MySQL server version](#mysql-server-version).
- **Database__JournalMode**: SQLite only, optional. The SQLite journal mode, see [SQLite journal mode](#sqlite-journal-mode).

### `appsettings.json`

The database settings are located under the `Database` key in the `appsettings.json` configuration file:

```json
{
    ...

    "Database": {
        "Type": "Sqlite",
        "ConnectionString": "Data Source=pagetto.db"
    },

    ...
}
```

These settings are related to the database configuration:

- **Type**: The database engine to use, this should be one of the strings from the above list such as `PostgreSql` or `Sqlite`.
- **ConnectionString**: The connection string for your database engine.
- **ServerVersion**: MySQL only, optional. The version of your MySQL or MariaDB server, see [MySQL server version](#mysql-server-version).
- **JournalMode**: SQLite only, optional. The SQLite journal mode, see [SQLite journal mode](#sqlite-journal-mode).

### MySQL server version

The MySQL provider needs to know the server version to generate the right SQL. By default PaGetto detects it by connecting to the server. Detection happens once per connection string and the result is reused by every request; if detection fails (for example because the server is not reachable yet), the next request tries again.

You can skip detection entirely by setting `ServerVersion` to your server's version, followed by `-mysql` or `-mariadb`:

```json
{
    ...

    "Database": {
        "Type": "MySql",
        "ConnectionString": "Server=mysql;Database=pagetto;User=pagetto;Password=...",
        "ServerVersion": "8.0.36-mysql"
    },

    ...
}
```

As an environment variable: `Database__ServerVersion=8.0.36-mysql` (for MariaDB, e.g. `11.4.2-mariadb`). PaGetto refuses to start if the value can't be parsed.

Why it helps under load: detecting the version takes a connection from the server's connection limit, and requests that arrive while detection is running (for example a burst of CI builds restoring at once) wait for it. If the server is struggling, a failed detection is retried by the next request. With `ServerVersion` set, PaGetto never opens a connection just to find out the version, so none of this applies.

### SQLite journal mode

`JournalMode` sets the SQLite [journal mode](https://www.sqlite.org/pragma.html#pragma_journal_mode). PaGetto runs `PRAGMA journal_mode=<value>;` after the migrations on every startup and logs the mode the database reports. SQLite stores the mode in the database file. Allowed values (case-insensitive): `DELETE`, `TRUNCATE`, `PERSIST`, `MEMORY`, `WAL`, `OFF`. PaGetto refuses to start with any other value.

When `JournalMode` is not set, PaGetto leaves the database as it is. Note that a database file that PaGetto creates from scratch is already in `WAL` mode (the Entity Framework Core default), while a database created by an older version or another tool usually uses `DELETE`.

```json
{
    ...

    "Database": {
        "Type": "Sqlite",
        "ConnectionString": "Data Source=/data/pagetto.db",
        "JournalMode": "WAL"
    },

    ...
}
```

As an environment variable: `Database__JournalMode=WAL`.

`WAL` lets readers and a writer work at the same time, which avoids most `database is locked` errors under concurrent pushes and restores. It creates two extra files next to the database (`pagetto.db-wal` and `pagetto.db-shm`). They belong to the database: never delete them while PaGetto runs, and note that copying only `pagetto.db` of a running instance can miss the most recent writes.

:::warning

WAL is not safe on network filesystems (NFS, SMB/CIFS and many Kubernetes `ReadWriteMany` volumes such as Azure Files or EFS) when processes on different hosts share the database file. WAL relies on shared memory, which only works between processes on the same host, so the database can be corrupted. If the SQLite file lives on such a volume, set `JournalMode` to `DELETE`, or better, run a single replica on a local (`ReadWriteOnce`) volume or use a server database such as PostgreSQL.

:::

## Storage configuration

The `Storage` section is required and selects where package files, symbols and the [Data Protection keys](upgrading.md#data-protection-keys) are stored. `Storage:Type` is one of:

| `Type` | Storage | Setup |
|---|---|---|
| `FileSystem` | A local folder or volume | [Docker](Installation/docker.md), [Local](Installation/local.md) |
| `AzureBlobStorage` | Azure Blob Storage | [Azure](Installation/azure.md) |
| `AwsS3` | Amazon S3 | [AWS](Installation/aws.md) |
| `GoogleCloud` | Google Cloud Storage | [Google Cloud](Installation/gcp.md) |
| `AliyunOss` | Alibaba Cloud OSS | [Alibaba Cloud](Installation/aliyun.md) |
| `TencentCos` | Tencent Cloud COS | [Tencent Cloud](Installation/tencent.md) |
| `Null` | Nothing is stored | For testing only |

For `Filesystem`, `Storage:Path` is the folder to store files in. When it is empty, PaGetto uses its current working directory.

```json
{
    ...

    "Storage": {
        "Type": "FileSystem",
        "Path": "/data"
    },

    ...
}
```

## Search configuration

The `Search` section is required. `Search:Type` is `Database` (search runs as queries against the configured database) or `Null` (search returns no results). The validator also accepts `AzureSearch`, but Azure Search is not currently supported.

```json
{
    ...

    "Search": {
        "Type": "Database"
    },

    ...
}
```

## IIS server options

IIS Server options can be configured under the `IISServerOptions` key. The available options are detailed at [docs.microsoft.com](https://docs.microsoft.com/dotnet/api/microsoft.aspnetcore.builder.iisserveroptions)

:::note

If not specified, PaGetto sets `MaxRequestBodySize` from [`MaxPackageSizeMiB`](#maximum-package-size) (8 GiB by default), rather than using the ASP.NET Core default of 30MB. You only need `IISServerOptions` to set a lower cap, as in the example below (250MB).

:::

```json
{
    ...

    "IISServerOptions": {
        "MaxRequestBodySize": 262144000
    },

    ...
}
```

## Health Endpoint

A health endpoint is exposed at `HealthCheck:Path` (`/health` in the shipped `appsettings.json`) that returns 200 OK or 503 Service Unavailable and always includes a json object listing the current status of the application:

```json
{
  "Status": "Healthy",
  "Sqlite": "Healthy",
  ...
}
```

The check covers the database only: package storage isn't checked, so a healthy response doesn't prove that packages can be read or written.

The services can be omitted by setting the `Statistics:ListConfiguredServices` to false, in which case only the `Status` property is returned in the json object.

The path and the name of the "Status" property are configurable. `Path` is required and must start with `/`; PaGetto refuses to start otherwise.

```json
{
    ...

    "HealthCheck": {
        "Path": "/healthz",
        "StatusPropertyName": "Status"
    },

    ...
}
```

## Maximum package size

The max package size defaults to 8192 MiB (8 GiB) and can be configured in MiB with the `MaxPackageSizeMiB` setting. The NuGet gallery currently has a 250 MB limit, which is enough for most packages. The older `MaxPackageSizeGiB` setting is still read when `MaxPackageSizeMiB` isn't set, with a warning at startup.
This can be useful if you are hosting a private feed and need to host large packages that include chocolatey installers, machine learning models, etc.

```json
{
    ...

    "MaxPackageSizeMiB": 8192,

    ...
}
```

A [feed](feeds.md#feed-settings) can set its own, lower limit. The server-wide value still caps every request, because it applies before PaGetto knows which feed a push is for, so a feed can't raise it. Pushes over a feed's limit are rejected with `413 Payload Too Large`.

## Registration page size

Packages with many versions are served as a paged [registration index](https://learn.microsoft.com/nuget/api/registration-base-url-resource).
If a package has more versions than `RegistrationPageSize` (default `64`), the registration index only links to its pages, and the NuGet client fetches each page separately.
Packages with fewer versions are returned in a single response, as before. The value must be at least `1`.

```json
{
    ...

    "RegistrationPageSize": 64,

    ...
}
```

## Upstream listing cache

Mirrored feeds keep upstream version lists and package metadata in memory for `UpstreamListingCacheSeconds` seconds (default `300`) instead of asking the upstream on every request. `0` turns the cache off; negative values are refused at startup. This is the default for feeds that don't set their own value. See [Upstream listing cache](feeds.md#upstream-listing-cache).

```json
{
    ...

    "UpstreamListingCacheSeconds": 300,

    ...
}
```

## CORS

By default, PaGetto allows cross-origin requests from any origin.
To restrict this, list the allowed origins in `Cors:AllowedOrigins`.
Set `AllowCredentials` to `true` only if a browser application on one of those origins must send cookies or an `Authorization` header. It requires `AllowedOrigins` to be set.

```json
{
    ...

    "Cors": {
        "AllowedOrigins": [ "https://portal.example.com" ],
        "AllowCredentials": false
    },

    ...
}
```

## Security headers

PaGetto adds the `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `X-Permitted-Cross-Domain-Policies` and `Permissions-Policy` headers to every response.
Set `SecurityHeaders:Enabled` to `false` if your reverse proxy already sets them.

`EnableHsts` sends the `Strict-Transport-Security` header (outside the Development environment).
Only enable it when PaGetto is always served over HTTPS, because browsers will then refuse plain HTTP for `HstsMaxAgeDays` days.

```json
{
    ...

    "SecurityHeaders": {
        "Enabled": true,
        "EnableHsts": false,
        "HstsMaxAgeDays": 365
    },

    ...
}
```

## Request rate limiting

PaGetto can limit how many requests each client makes, to slow down brute-force attempts and misbehaving or misconfigured clients.
It is off by default. When enabled, every client gets `PermitLimit` requests per fixed window of `WindowSeconds` seconds:

- Authenticated requests are counted per user name.
- Anonymous requests (including ones with invalid credentials) are counted per client IP address.

A request over the limit gets `429 Too Many Requests` with a `Retry-After` header (in seconds).
`QueueLimit` lets that many extra requests wait for the next window instead of being rejected.
The liveness probe (`/livez`) and the [health endpoint](#health-endpoint) are never limited.

```json
{
    ...

    "RequestRateLimit": {
        "Enabled": true,
        "PermitLimit": 600,
        "WindowSeconds": 60,
        "QueueLimit": 0
    },

    ...
}
```

A single `dotnet restore` of a large solution can send hundreds of requests in a few seconds, so don't set `PermitLimit` too low.

:::warning

Behind a reverse proxy, PaGetto sees the proxy's address unless it reads the client IP from the `X-Forwarded-For` header.
PaGetto currently trusts that header from any sender, so a client that can reach PaGetto directly (or through a proxy that passes the header through unchanged) can set it to any value and get a fresh budget for each fake address.
Make sure PaGetto is only reachable through your proxy and that the proxy overwrites `X-Forwarded-For`, or treat the anonymous limit as best effort.

:::

## HTTP caching

PaGetto sets caching headers on a few endpoints. There is nothing to configure.

- Registration responses (`/v3/registration/...`) and package version lists (`/v3/package/{id}/index.json`) are sent with `Cache-Control: private, no-cache` and an `ETag` derived from the response content. A client that repeats the request with a matching `If-None-Match` header gets `304 Not Modified` without the body.
- Package icons are sent with `Cache-Control: private, max-age=3600`, so browsers reuse them for an hour.

Responses are never marked `public` or `immutable`: feeds can require authentication, and a feed that allows package overwrites can change the content of an existing version. Make sure your reverse proxy doesn't cache these responses in a shared cache.

## Response compression

PaGetto compresses JSON, CSS, JavaScript and SVG responses with Brotli or gzip, whichever the client accepts, over HTTP and HTTPS. There is nothing to configure. This makes the NuGet API responses (search, registrations, version lists) much smaller.

HTML pages aren't compressed on purpose: compressing pages that carry anti-forgery tokens over HTTPS enables [BREACH](https://en.wikipedia.org/wiki/BREACH)-style attacks. Package downloads (`.nupkg`, `.snupkg`) are already compressed and are sent as they are.

## Audit log

PaGetto writes one log line for every package upload, unlist, delete and relist, from NuGet clients and from the web UI, including denied attempts, and one for every change on the **Admin** pages. There is no separate audit store: the lines go to the normal logs, so you can collect them with whatever already reads PaGetto's output.

### Package events

```
AUDIT package_upload_succeeded feed=default package_id=Contoso.Utils package_version=1.2.0 actor=alice ip=10.0.0.12
```

| Field | Value |
|---|---|
| Event | NuGet API: `package_upload_{succeeded,unauthorized,read_only}` and `package_{delete,relist}_{succeeded,unauthorized,read_only,not_found}`, plus `package_upload_already_exists`, `package_upload_invalid_package` and `package_upload_too_large`. A symbol package over the feed's size limit logs `symbol_upload_too_large` (with `feed`, `actor` and `ip` only). Web UI (the package page's **Manage** section and **Relist** links): `package_{unlist,relist,delete}_{succeeded,unauthorized,read_only,not_found}`, where `package_delete_*` is a hard delete. Browser uploads on the **Upload** page log the same `package_upload_*` events as the API, and `symbol_upload_{succeeded,unauthorized,read_only,invalid_package,package_not_found,too_large}` for symbol packages, with the package id and version |
| `feed` | The feed slug |
| `package_id`, `package_version` | Empty for uploads that are denied before the package is read |
| `actor` | The user name (the token owner for personal access tokens), `api-key` for the shared API key in `Legacy` mode, or `anonymous` |
| `ip` | The client IP address. Behind a reverse proxy it comes from `X-Forwarded-For`, with the same caveat as [rate limiting](#request-rate-limiting). |

The `*_unauthorized` events cover both denial responses: `401 Unauthorized` for missing or wrong credentials, and `403 Forbidden` for a signed-in user without the push or delete permission.

### Administration events

```
AUDIT account_disabled target=bob detail= actor=admin ip=10.0.0.12
```

| Event | `target` | `detail` |
|---|---|---|
| `account_created` | Username | `web_sign_in=True` or `False` |
| `account_enabled`, `account_disabled` | Username | |
| `account_web_access_granted`, `account_web_access_revoked` | Username | |
| `account_admin_granted`, `account_admin_revoked` | Username | |
| `account_unlocked`, `account_password_reset`, `account_deleted` | Username | |
| `account_token_created` | Username | Token prefix and expiry date |
| `group_created`, `group_deleted` | Group name | |
| `group_member_added`, `group_member_removed` | Group name | `user=<username>` |
| `feed_permission_set` | Group name | `feed=<slug> pull=… push=… delete=…` |
| `feed_permission_revoked` | Group name | `feed=<slug>` |
| `feed_created`, `feed_deleted` | Feed slug | |
| `feeds_reordered` | `feeds` | |
| `feed_settings_updated` | Feed slug | Number of mirrors |
| `feed_mirror_credentials_changed` | Feed slug | Mirror source and which secret changed (`password` or `token`), never the value |

`actor` and `ip` are the same as for package events.

### Log categories

Successful operations are logged at `Information`, denials and failures at `Warning`. NuGet API lines use the `PaGetto.Web.Controllers.PackagePublishController` log category, web UI lines use `PaGetto.Web.Audit.WebAuditLog`. The default `appsettings.json` logs both at `Information`. If you override the `Logging` section, keep them at `Information` or the successful operations won't be logged:

```json
{
    ...

    "Logging": {
        "Console": {
            "LogLevel": {
                "PaGetto.Web.Controllers.PackagePublishController": "Information",
                "PaGetto.Web.Audit.WebAuditLog": "Information",
                "Default": "Warning"
            }
        }
    },

    ...
}
```

## Statistics

The statistics page shows, for the current feed, the number of packages and versions, total downloads, the stored size, the most downloaded and the most recently published packages, and optionally the services in use.
You can hide or show this page by modifying the `EnableStatisticsPage` configuration (default `true`).
`ListConfiguredServices` controls whether the services in use for database and storage (such as `Sqlite`) are listed on the stats page and in the [health endpoint](#health-endpoint) response. It defaults to `false`, but the shipped `appsettings.json` sets it to `true`:

```json
{
    ...

    "Statistics": {
        "EnableStatisticsPage": true,
        "ListConfiguredServices": false
    },

    ...
}
```



## Email

PaGetto can send emails. This is currently used for [personal access token expiry notifications](authentication.md#expiry-notifications).

Email is **disabled by default**. Enable it by adding an `Email` section and setting `Type` to a backend:

| Backend | `Type` | Notes |
|---------|--------|-------|
| SMTP | `Smtp` | Delivers over SMTP using MailKit. |
| Microsoft Graph | `Graph` | Sends via `users/{id}/sendMail`. Requires the `Mail.Send` application permission on the identity used. Works with any storage provider. |
| Disabled | `Null` (or the section omitted) | Drops all messages. The default. |

### SMTP

```json
{
    ...

    "Email": {
        "Type": "Smtp",
        "FromAddress": "nuget@example.com",
        "FromName": "PaGetto",
        "Host": "smtp.example.com",
        "Port": 587,
        "UseStartTls": true,
        "Username": "",
        "Password": ""
    },

    ...
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `FromAddress` | -- | Address messages are sent from. |
| `FromName` | -- | Display name messages are sent from. |
| `Host` | -- | SMTP server host name. |
| `Port` | `587` | SMTP server port. |
| `UseStartTls` | `true` | When `true`, upgrade the connection with STARTTLS; otherwise MailKit auto-negotiates the most secure option the server supports. |
| `Username` | -- | Optional. When empty, no authentication is attempted. |
| `Password` | -- | Optional SMTP password. |

:::warning

PaGetto refuses to send SMTP credentials over an unencrypted connection. When `Username` is set, either keep `UseStartTls` enabled or use a server that supports TLS, otherwise sending fails.

:::

### Microsoft Graph

Requires the Azure provider (`app.AddGraphEmail()`, wired up by default in the PaGetto host). The message is sent from the `SenderUserId` mailbox; `FromAddress`/`FromName` apply to SMTP only.

```json
{
    ...

    "Email": {
        "Type": "Graph",
        "SenderUserId": "nuget@example.com",
        // Optional client-secret auth (e.g. local dev). Omit all three to use the
        // deployed managed identity (DefaultAzureCredential).
        "TenantId": "",
        "ClientId": "",
        "ClientSecret": ""
    },

    ...
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `SenderUserId` | -- | The mailbox to send as: a user's object id or user principal name. |
| `TenantId` | -- | Optional tenant id for client-secret authentication. |
| `ClientId` | -- | Optional client (application) id for client-secret authentication. |
| `ClientSecret` | -- | Optional client secret. |

When `TenantId`, `ClientId`, and `ClientSecret` are all set, a client-secret credential is used; otherwise `DefaultAzureCredential` (the deployed managed identity) is used.

:::info

All email settings can be provided via environment variables (`Email__Type`, `Email__Host`, ...) or [Docker secrets](#load-secrets-from-files) (e.g. `/run/secrets/Email__Password`).

:::

## Load secrets from files

Mostly useful when running containerised (e.g. using Docker, Podman, Kubernetes, etc), the application will look for files named in the same pattern as environment variables under `/run/secrets`.

```shell
/run/secrets/Database__ConnectionString
```

This allows for sensitive values to be provided individually to the application, typically by bind-mounting files.

### Docker Compose example

```yaml
services:
  pagetto:
    image: letreset/pagetto:latest
    volumes:
      # Single file mounted for API key
      - ./secrets/api-key.txt:/run/secrets/Authentication__ApiKeys__0__Key:ro
      - ./data:/srv/pagetto
    ports:
      - "5000:8080"
    environment:
      - Database__ConnectionString=Data Source=/srv/pagetto/pagetto.db
      - Database__Type=Sqlite
      - Authentication__Mode=Legacy
      - Storage__Type=FileSystem
      - Storage__Path=/srv/pagetto/packages
```

The specified file `./secrets/api-key.txt` contains the clear text api key only.

The port mapping will make available the service at `http://localhost:5000`. (To make it available using `https` you should use an additional reverse proxy service, like "apache" or "nginx".)

Instead of targeting the `latest` version you may also refer to tags for major, minor and fixed releases, e.g. `1`, `1.0` or `1.0.0`. See [Image tags](Installation/docker.md#image-tags).

Aditional documentation for secrets:

- [How to use secrets in Docker Compose](https://docs.docker.com/compose/use-secrets)
- [Docker Swarm secrets](https://docs.docker.com/engine/swarm/secrets)
- [Kubernetes secrets](https://kubernetes.io/docs/concepts/configuration/secret)
- [ASP.NET Core Documentation](https://docs.microsoft.com/aspnet/core/fundamentals/configuration/#key-per-file-configuration-provider)
