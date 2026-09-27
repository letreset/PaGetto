# Migrating from BaGetter

PaGetto can take over an existing BaGetter installation in place: point PaGetto at your BaGetter database and package storage, and your packages, API keys and client URLs keep working. On the first start PaGetto migrates the database to its own schema.

## Before you start

**Back up the database and the package storage.** The migrations add the feed, user, group, permission and token tables, and move every existing package into the default feed. There is no way back to BaGetter once they have run.

If you use Azure Table Storage as the database (`Database:Type` = `AzureTable`), move to a SQL database first; see [Azure Table Storage](#azure-table-storage-isnt-supported).

## Step by step

1. Stop BaGetter and back up the database and storage.
2. Replace the application:
   - **Docker:** change the image from `bagetter/bagetter` to [`letreset/pagetto`](https://hub.docker.com/r/letreset/pagetto) and pin a version, for example `letreset/pagetto:1.0.0`. Keep mounting the same `/data` volume.
   - **Zip / IIS:** download `pagetto-<version>.zip` from the [releases page](https://github.com/letreset/PaGetto/releases), extract it into a new folder, copy your `appsettings.json` over, and run `dotnet PaGetto.dll` instead of `dotnet BaGetter.dll`.
   - **Kubernetes:** install the [PaGetto chart](Installation/kubernetes.md) and point it at your existing volume and database.
3. Check the names that changed (next section). The one that most often matters is the SQLite file name.
4. Start PaGetto and watch the log for migration errors, and for `Copying global Mirror configuration to default feed` if you use a mirror.
5. Browse to the site, sign in again, and restore and push a package to confirm everything works.

## What has a new name

Configuration keys (`Database`, `Storage`, `Search`, `Mirror`, `ApiKey`, …) are the same as in BaGetter, so your `appsettings.json` and `Section__Key` environment variables keep working. Only these names changed:

| BaGetter | PaGetto | What to do |
|---|---|---|
| Image `bagetter/bagetter` | `letreset/pagetto` | Change the image |
| `BaGetter.dll` | `PaGetto.dll` | Change your start command, service or IIS site |
| Default SQLite file `bagetter.db` (Docker: `/data/db/bagetter.db`) | `pagetto.db` (Docker: `/data/db/pagetto.db`) | If you relied on the default, either rename the file or set `Database__ConnectionString=Data Source=/data/db/bagetter.db` |
| `BAGET_CONFIG_ROOT` environment variable | `PAGETTO_CONFIG_ROOT` | Rename the variable if you use it |
| Sign-in cookie | `PaGetto.Auth` | Nothing: everyone signs in again once |

Package storage paths are not renamed, so the files stay where they are.

## Feeds and URLs

Every package now belongs to a [feed](feeds.md). On the first start PaGetto creates the **default feed** (slug `default`) and puts all your existing packages in it.

- The default feed stays at the old URLs, for example `https://your-server/v3/index.json`. Existing `nuget.config` files and CI pipelines keep working.
- New feeds live under `/feeds/{slug}/`, for example `https://your-server/feeds/internal/v3/index.json`.
- New packages in the default feed are stored under `packages/default/…`. Packages pushed by BaGetter stay at their old path (`packages/{id}/…`) and are still found, so you don't need to move files.

## Mirror settings move to the feed

The global `Mirror` section is **obsolete**. On the first start, if it is enabled, PaGetto copies it (source, legacy flag, timeout and upstream authentication) to the default feed as its first mirror. From then on each feed's own mirror settings, edited in **Admin > Feeds**, apply, and changing `Mirror` in `appsettings.json` has no effect.

Don't remove the `Mirror` section: PaGetto refuses to start without it. Once the copy has happened, set `Mirror:Enabled` to `false`. Otherwise PaGetto copies it to the default feed again whenever that feed has no mirrors left, for example after you remove its mirror on purpose.

A feed can have [several mirrors](feeds.md#multiple-mirrors). Mirrored feeds keep upstream version lists in memory for 5 minutes by default, so a version newly published upstream can take up to 5 minutes to appear; set `UpstreamListingCacheSeconds` to `0` to turn this off. See [Upstream listing cache](feeds.md#upstream-listing-cache).

## Feed settings override the global ones

These settings can be set per feed. The values in configuration become the **defaults** for feeds that don't override them:

- `AllowPackageOverwrites`
- `PackageDeletionBehavior`
- `IsReadOnlyMode`
- `MaxPackageSizeGiB`
- `Retention` (max major, minor, patch and prerelease versions)

Nothing changes until you override a setting on a feed. See [Feed settings](feeds.md#feed-settings).

Retention now runs as soon as any of the four limits is set, not only `MaxMajorVersions`. If you set `MaxMinorVersions`, `MaxPatchVersions` or `MaxPrereleaseVersions` without `MaxMajorVersions`, the next push or mirror of each package deletes the versions outside the limits. Check your [retention settings](configuration.md#package-auto-deletion) before you migrate.

## Authentication

`Authentication:Mode` selects how people sign in. The default is `Config`, which works like BaGetter: `ApiKey`/`ApiKeys` protect pushes and `Credentials` protect reads. If you do nothing, authentication works as before.

| Mode | Use it when |
|---|---|
| `Config` | You want BaGetter's behavior (the default) |
| `Local` | You want user accounts, groups and per-feed permissions stored in PaGetto |
| `Entra` | Everyone signs in with Microsoft Entra ID |
| `Hybrid` | You want Entra ID for people and local accounts for build agents or external users |

Switching away from `Config` turns off anonymous access, `ApiKey` and `Credentials`. Plan the switch before you make it; see [Authentication](authentication.md). In the user modes, a valid account or token without the push or delete permission gets `403 Forbidden` instead of `401 Unauthorized`.

## Data Protection keys

PaGetto keeps its ASP.NET Core Data Protection keys (which protect sign-in cookies and forms) in the configured package storage, at `dataprotection/keyring.xml`. With file system storage in Docker this is inside `/data`. If `/data` isn't a persistent volume, every restart signs everybody out, and several replicas can't share cookies.

## Database changes

The migrations run automatically on startup (`RunMigrationsAtStartup`, on by default). Some of them need attention on a large or older database:

- **Usernames and group names must be unique regardless of case.** This only matters if you already have accounts; a BaGetter database has none, so the check passes.
- **MySQL moves to utf8mb4.** BaGetter stored MySQL data as `latin1`, so package metadata with other characters (for example the author "Havlíček", or Polish or Turkish text) failed to push or mirror. A migration converts the database and every table to `utf8mb4` with the `utf8mb4_unicode_ci` collation and keeps the data. It rewrites every table (one `ALTER TABLE` each), which can take a while and blocks writes to the table being converted, so plan a maintenance window. The tables use the `DYNAMIC` row format (the default on MySQL 5.7.9+, MySQL 8 and MariaDB 10.2+), and the nine long package columns become `text`.
- **PostgreSQL: package versions become case-insensitive.** Some older databases still have `Packages.Version` as `varchar(64)` although the migration that should have made it `citext` is recorded as applied, so prerelease versions with capital letters can't be found or deleted. PaGetto repairs this on startup. If the log says `Cannot convert "Packages"."Version" to citext`, delete one version of each pair that only differs by case and start again.
- **Stored packages are read once.** PaGetto fills the new `Copyright`, `LicenseExpression` and `Size` columns in the background after startup, 100 packages at a time, while it keeps serving requests. On cloud storage this means one read of every stored package.

## Azure Table Storage isn't supported

The `AzureTable` database type can't store feeds, users, groups, permissions or tokens, so PaGetto refuses to start with it. Before you migrate, move to one of the SQL databases (`Sqlite`, `SqlServer`, `PostgreSql` or `MySql`): start BaGetter with the new database and the same storage, and push your packages again (see [Import packages from a local feed](Import/local-feeds.md)).

## New settings you may want

These are optional and off or safe by default. See [Configuration](configuration.md).

- `RegistrationPageSize`: registration index paging for packages with many versions (default 64).
- `Cors`: allowed origins for browser clients.
- `SecurityHeaders`: security headers (on by default) and optional HSTS.
- `RequestRateLimit`: per-client request rate limiting (off by default).
- `Database:ServerVersion` (MySQL): skips server version detection.
- `Database:JournalMode` (SQLite): sets the journal mode, e.g. `WAL`.
- `Email` and `PatExpiryNotification`: emails before personal access tokens expire.
- The [machine-wide config file](configuration.md#machine-wide-config-file): `%ProgramData%\PaGetto\appsettings.json` or `/etc/pagetto/appsettings.json`.
