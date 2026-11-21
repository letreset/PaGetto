# AGENTS.md

Guidance for AI agents working in this repository.

## Guidelines for AI Agents

### 1. Think Before Coding
**Don't assume. Don't hide confusion. Surface tradeoffs.**
- State your assumptions explicitly. If you're uncertain, ask.
- If there are several interpretations, present them. Don't pick one silently.
- If a simpler approach exists, say so. Push back when it's warranted.
- If something is unclear, stop, name what's confusing, and ask.

### 2. Simplicity First
**Write the minimum code that solves the problem. Nothing speculative.**
- No features beyond what was asked, no abstractions for single-use code, and no configurability that wasn't requested.
- No error handling for impossible scenarios.
- Test: would a senior engineer call this overcomplicated? If so, simplify.

### 3. Surgical Changes
**Touch only what you must. Clean up only your own mess.**
- Don't "improve" adjacent code, comments or formatting, and don't refactor what isn't broken. Match the existing style.
- Mention unrelated dead code instead of deleting it. Do remove imports and variables that *your* change made unused.
- Every changed line should trace back to the request.

### 4. Goal-Driven Execution
**Define success criteria. Loop until verified.**
- "Fix the bug" means: write a test that reproduces it, then make it pass. "Refactor X" means: tests pass before and after.
- For multi-step tasks, state a brief plan with a verification check per step.

---

## What is PaGetto

A lightweight package server: ASP.NET Core on .NET 10, with pluggable database, storage and search backends. It started as a NuGet and symbol server and now hosts one ecosystem per feed (see `FeedType`). This repo is **letreset/PaGetto**: multi-feed, Local/Entra/OIDC/Hybrid auth with per-feed permissions, an admin UI.

## Branches, versions, releases

- `main` is the only long-lived branch. Work happens on `feature/*` branches.
- Always do your work on a new branch in its own git worktree (e.g. `git worktree add ../PaGetto-<topic> -b feature/<topic> origin/main`), never directly in the main checkout. Every task gets its own worktree and branch.
- Once the branch's PR is merged, clean up: remove the worktree (`git worktree remove <path>`), delete the local branch (`git branch -D feature/<topic>`, since rebase merges leave it looking unmerged) and prune stale references (`git worktree prune`, `git fetch --prune`).
- `main` is protected by a repository ruleset: no direct pushes, no merge commits (linear history), and changes land only through a pull request in letreset/PaGetto. The PR needs the required checks to pass (Build & test on ubuntu and windows, Docker build, Helm lint, CodeQL) and its review threads resolved; no approval is required.
- To ship a branch: rebase it on `origin/main`, push it, open a PR against `main`, and merge it with **Rebase and merge**, so each Conventional Commit stays separate for the git-cliff changelog. Use squash only for a branch that is really one change. Pushing `main` directly, or a `--no-ff` merge, is rejected.
- Versioning is semver starting at **1.0.0**.
- Pushing a `vX.Y.Z` tag on `main` runs `.github/workflows/release.yml`. It runs the tests, creates a GitHub release with a zip and a git-cliff changelog, pushes the Docker image `letreset/pagetto` to Docker Hub, pushes the Helm chart to `oci://ghcr.io/letreset/charts`, and pushes the `PaGetto.*` library packages (everything under `src/` except the host) to nuget.org through Trusted Publishing (`NUGET_USER` secret holds the nuget.org profile name; no API key is stored).
- A `-` in the tag (e.g. `v1.1.0-rc.1`) marks a prerelease, which does not move the `latest` image tag.
- Release notes credit contributors by GitHub username (`@nick`), never by real name. `cliff.toml` resolves authors through the GitHub API (`[remote.github]`, needs `GITHUB_TOKEN`); keep it that way, and use `@nick` when writing or editing release notes by hand.
- Use [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `ci:`, …). `cliff.toml` groups the changelog by these prefixes.
- The roadmap lives in GitHub issues on letreset/PaGetto, one issue per task.

## Code navigation (CodeGraph)

The repo is indexed by CodeGraph (`.codegraph/` is local to each machine; only `.codegraph/.gitignore` is committed). When the index exists, use it before grep or reading whole files:

- **MCP tools:** `codegraph_explore` returns the current, line-numbered source of the symbols you name plus the call paths between them, including DI and interface-to-implementation hops that grep can't follow. `codegraph_callers` lists who calls a symbol, `codegraph_impact` what a change affects, `codegraph_files` the indexed file tree.
- **Always pass `projectPath`** (the repo root, e.g. `C:\Users\<you>\source\repos\PaGetto`). The server has no default project, and calls without it fail with "expected string, received undefined".
- Name symbols or files in the query, e.g. `"FeedResolutionMiddleware IFeedContext CurrentFeed"`. One call covers about six files; spend a second call on the uncovered area rather than reading files.
- The index lags behind uncommitted edits: files marked "changed on disk" come back without source, so read those directly, and re-read any file before editing it. Grep is still the tool for string literals, config keys, `.cshtml`, JSON and YAML.
- If the MCP server doesn't connect, the CLI (`codegraph explore "<symbols or question>"`) prints the same output when it is on the `PATH`; otherwise fall back to grep and reading.

## Build & test

The usual `dotnet restore`/`build`/`test` from the repo root; the SDK is pinned in `global.json`. `dotnet run --project src/PaGetto` serves http://localhost:5000.

EF migrations need one per provider (Sqlite, SqlServer, PostgreSql, MySql):
```bash
dotnet ef migrations add Name --project src/PaGetto.Database.Sqlite --startup-project src/PaGetto
```

## Architecture

### Provider pattern
Storage, database, search and email use `IProvider<T>`. Every implementation is registered (via `TryAdd*`), and the configuration (`Database:Type`, `Storage:Type`, …) picks the active one at runtime through `DependencyInjectionExtensions.GetServiceFromProviders<T>`.

### Multi-feed
- The `Feed` entity (`Core/Entities/Feed.cs`) holds per-feed overrides: overwrite and deletion behavior, read-only mode, max package size, and retention. Its **mirrors live in the DB rather than in config**: `Feed.Mirrors` is an ordered list of `FeedMirror` rows (source, legacy flag, timeout, upstream auth, enabled). `IFeedSettingsResolver` merges a feed's overrides with the global `PaGettoOptions`.
- `FeedResolutionMiddleware` maps `/feeds/{slug}/…` to that feed by moving the slug into `PathBase`. Any other path resolves to the default feed (`Feed.DefaultSlug = "default"`). Controllers read `IFeedContext.CurrentFeed`, and services take `feedId`/`feedSlug`.
- Because the slug is in `PathBase`, routes and `PaGettoUrlGenerator` stay feed-agnostic. Add a route once and it works for every feed.
- Storage paths are `packages/{feedSlug}/{id}/{version}/…` and `symbols/{feedSlug}/…`.
- Upstream clients come from `UpstreamClientFactory.CreateForFeed(feed)`: one enabled mirror gives a plain V2/V3 client, several give a `FallbackUpstreamClient` (merged version lists, download from the first mirror that has the package, failing mirrors skipped). Unless the feed's `UpstreamListingCacheSeconds` is 0, that client is wrapped in `CachingUpstreamClient`, which keeps version/metadata listings in the singleton `UpstreamListingCache` (an empty one for `UpstreamListingCache.MissDuration` at most, and not at all when a mirror failed: upstream clients call `UpstreamListingCache.ReportFailure` where they skip a failed mirror), keyed by feed id, `Feed.UpdatedAtUtc` (so saving the feed invalidates) and lowercased package id. Concurrent misses for a key share one upstream call, which runs with `CancellationToken.None` so one caller leaving doesn't cancel it for the others. `PackageService.MirrorAsync` serializes concurrent mirroring of the same `(feed, id, version)` through the singleton `PackageMirrorLock`. Every `HttpClient` from `UpstreamClientFactory.CreateHttpClient` shares one static `SocketsHttpHandler` (pooled connections, no cookies), so callers create and dispose a client per request with that mirror's timeout and headers.

### Feed types and ecosystems
- `Feed.Type` (`FeedType`: `NuGet` = 0, `Pub` = 1, `Npm` = 2, `Maven` = 3, `PyPI` = 4, `Cargo` = 5, `Go` = 6, `RubyGems` = 7, `Hex` = 8, `Composer` = 9, `CocoaPods` = 10, `Swift` = 11, `Cran` = 12, `Ivy` = 13, `Vsix` = 14, `VsCode` = 15) is set when a feed is created and never changes. The values are stored in the database. The default feed is always a NuGet feed.
- **NuGet** keeps its own model (`Package` and its child tables, `IPackageDatabase`, `IPackageStorageService`, search providers). Don't put other ecosystems into those tables: NuGet version normalization drops build metadata, which other ecosystems treat as part of the version.
- **Every other ecosystem** stores its packages as artifacts: `Artifact` (feed, normalized name) and `ArtifactVersion` (version, lowercased `NormalizedVersion`, `Listed`, `Description`, the manifest as `MetadataJson`, SHA-256 `ContentHash`, `Size`, `Downloads`, `HasReadme`, `CachedFrom`, `Published`) `ArtifactTag` (a named pointer to a version, e.g. an npm dist-tag; removed with its version) and `ArtifactFile` (one file of a version that has several, e.g. a Maven POM and JAR or the wheels and sdist of a Python version, with its SHA-1, MD5, SHA-256 and SHA-512; removed with its version). `Core/Artifacts/` has what they share: `IArtifactDatabase` (reads are untracked), `ArtifactFileContent` (a file read into a temporary file with its checksums), `IArtifactStorageService` (`packages/{feedSlug}/{name}/{version}/archive` and `/readme`, and `/files/{fileName}`; a `:` in the name is a folder) `ArtifactVersionOrder` (version order per feed type), `ArtifactPublisher` (stores a version's archive and adds the version, removing the archive again when adding fails, since the storage refuses to replace content) and `ArtifactIndexCache` (the singleton store of indexes built from all of a feed's packages: RubyGems `/versions` and `/names`, Hex `/names` and `/versions`, CRAN `PACKAGES(.gz)`, the VSIX `atom.xml`). Every change through `ArtifactDatabase` raises `Feed.ArtifactRevision`, and a cached index is keyed by it and `Feed.UpdatedAtUtc`, so the next request after a change builds it again on every replica; entries also expire, after `UpstreamListingCacheSeconds` when the index includes the mirrors' listings.
- **Quarantine** (`Core/Quarantine/`): `Package` and `ArtifactVersion` have a global EF query filter `QuarantinedAtUtc == null`, so a quarantined version is invisible to every read (downloads, listings, search, indexes) without per-ecosystem code. Code that must see one says `IgnoreQueryFilters()`: the vulnerability scanner, the hard deletes, the write helper `ArtifactDatabase.Versions` (a version waiting in quarantine still gets its files and tags), and the admin `QuarantineService`. `QuarantinePolicy` applies the feed's policies (`Feed.QuarantineSeverity`, `Feed.QuarantineMaliciousPackages`) in `ArtifactDatabase.AddVersionAsync` and `PackageDatabase.AddAsync`, and after each scan. `PromotionService` copies a stored version to another feed of the same type (`POST {feed}/api/v1/promote`, the `/Promote` page), checked by `PromotionAuthorizer` (Web).
- **SBOM** (`Core/Sbom/`): `SbomService` makes a CycloneDX 1.6 JSON document of a stored version of any feed type (`GET {feed}/api/v1/sbom/{id}/{version}`, `SbomController`, **Download SBOM** on the package pages). `SbomPackageReader` reads the name, license, checksums and declared dependencies from the same manifest summaries the package pages use, `PackageUrl` makes the package URLs, and `CycloneDxWriter` writes the document with the version's vulnerability findings. A new ecosystem needs a case in `SbomPackageReader` and `PackageUrl`.
- **Each ecosystem's details** (its `Core/<Name>/` services, version and name rules, storage, mirroring, controller routes, client quirks, Connect page) are in `.claude/skills/pagetto-ecosystems/SKILL.md`. Read that ecosystem's section before changing or debugging it.
- A controller or Razor page model that belongs to one ecosystem carries `[FeedType(...)]`. `FeedTypeMiddleware` (after authorization) answers 404 when it is requested on a feed of another type. With user accounts it lets anonymous requests through to the endpoint's own challenge, so the feed type can't be probed. `NugetBasicAuthenticationHandler` picks the challenge from the endpoint: `Bearer realm="pub"` for pub endpoints, `Basic realm="PaGetto"` for Maven, Ivy, PyPI, Go, RubyGems, Hex, Composer, CocoaPods, CRAN, VSIX and VS Code endpoints (sbt credentials name the realm), a 401 without `WWW-Authenticate` and with a problem detail for Swift endpoints (Swift Package Manager hangs on Linux on a challenge), `Cargo login_url="…/connect"` for Cargo endpoints, `Basic realm="NuGet Server"` otherwise.
- Rules every ecosystem shares live in one place: `FeedSettingsResolverExtensions.CanOverwrite` (the overwrite policy; Composer development versions, Maven and Ivy snapshots add their own exception on top) and `GetMaxPackageSizeBytes`, and `FeedChangeAuthorizer` (Web, scoped), which decides whether a publish, yank or delete may change the current feed: the API key in `Legacy` mode, the user's push or delete permission otherwise, then read-only mode. Each controller answers `FeedChangeAccess.Unauthorized` and `ReadOnly` in its client's own format, and writes its audit lines with `WebAuditLog.PackageApiAsync`, which names the actor. Uploads read through `HttpRequestExtensions.GetUploadStreamOrNullAsync` (NuGet, symbols, pub) or check `Content-Length` themselves, so a body over the feed's limit is refused before it is read.
- **To add an ecosystem** (e.g. Composer): add a `FeedType` value, a `Core/<Name>/` folder with its version rules, archive reader and package service on top of the artifact services, a case in `ArtifactVersionOrder`, `FeedTypeNames`, `SbomPackageReader` and `PackageUrl`, a controller with `[FeedType]` and its routes in `PaGettoEndpointBuilder`, and the ecosystem's parts of `Pages/Artifact` and Connect, and its section in `.claude/skills/pagetto-ecosystems/SKILL.md`. No migration is needed unless the artifact tables lack something.

### Authentication & authorization
- `Authentication:Mode` is required (startup fails without it) and is one of `Legacy` (config-file `Authentication:ApiKeys`/`Credentials`, formerly `Config`, which is still accepted; the old top-level `ApiKey` fails validation), `Local`, `Entra`, `Oidc` or `Hybrid`. Single sign-on goes through one provider, `NugetAuthenticationOptions.SingleSignOnProvider`: Entra ID (`Authentication:Entra`, scheme `EntraOidc`) or a generic OpenID Connect provider (`Authentication:Oidc`, scheme `Oidc`; in `Hybrid` mode it wins when that section is set).
- The `NugetBasicAuth` scheme is the default. It forwards to the cookie scheme (`PaGetto.Auth`, sliding expiry of `Authentication:SessionTimeoutMinutes`, default 60) when a cookie is present without an `Authorization` header, which separates browsers from client tools.
- `IFeedAuthenticationService` authenticates by PAT (`AuthenticateByTokenAsync`) or by username/password (`AuthenticateByCredentialsAsync`). Passwords use bcrypt; tokens are stored as prefix + hash. A PAT is accepted in the `X-NuGet-ApiKey` header, as the Basic password, as a Bearer token (what `dart pub` and the npm clients send), or as the whole `Authorization` header when it starts with the token prefix (what Cargo sends). With a PAT as the Basic password, the username must be the token's owner or `__token__` (`FeedAuthenticationService.TokenUsername`, what Python clients send).
- `FeedPermissionHandler` enforces per-feed permissions (pull/push/delete) for the current feed. User permissions come from groups via `PermissionService`, and `SingleSignOnSyncService` provisions single sign-on users (`User.AuthProvider` `Entra` or `Oidc`, keyed by `EntraObjectId`, which holds the Entra `oid` or the OIDC `sub`) and syncs the token's role claim into role-linked groups and the admin flag.

### HTTP pipeline (`Startup.Configure`, in order)
ForwardedHeaders → PathBase → HSTS (optional) → `SecurityHeadersMiddleware` → ResponseCompression → `/livez` → `FeedStaticFilePathMiddleware` → StaticFiles → `VsixGalleryTokenMiddleware` → `VsCodeGalleryTokenMiddleware` → Authentication → RateLimiter (only when `RequestRateLimit:Enabled`) → `FeedResolutionMiddleware` → Routing → Authorization → `FeedTypeMiddleware` → CORS → `OperationCancelledMiddleware` (maps `OperationCanceledException` to 409) → `MaliciousPackageMiddleware` (answers a blocked download with 403) → endpoints → health check (`HealthCheck:Path`).

## Configuration

`PaGettoOptions` (`Core/Configuration/`) is bound from the config root. Sources, later ones winning: `appsettings*.json` (from `PAGETTO_CONFIG_ROOT` if set), user secrets, the optional machine-wide file (`%ProgramData%\PaGetto\appsettings.json` or `/etc/pagetto/appsettings.json`, see `Program.AddPlatformConfigFile`), environment variables, command line, `/run/secrets` (key-per-file). `ValidatePaGettoOptions` fails startup on invalid values.

Main keys:
- `Database`, `Storage`, `Search`: each has a `Type`. `Database:ServerVersion` (MySQL only, optional) skips server version detection, which otherwise runs once per connection string (`MySqlServerVersionResolver`).
- `Authentication`: `Mode`, `Entra`, `Oidc`, token and lockout limits, `SessionTimeoutMinutes` (cookie, default 60) and `MinPasswordLength` (default 12, 8 to 72). In `Local`/`Hybrid`, `InitialAdminSeeder` creates the local admin `admin`/`admin` at startup, after migrations, while no admin exists; `User.MustChangePassword` keeps it on `/Account/ChangePassword` (`MustChangePasswordMiddleware`) and out of basic auth until the password is changed.
- `MaxPackageSizeMiB` (default 8192; the legacy `MaxPackageSizeGiB` is still read via `PaGettoOptions.EffectiveMaxPackageSizeMiB`), `ForwardedHeaders` (`KnownProxies`, `KnownNetworks`; empty trusts every client), `DataProtection` (`CertificatePath`+`CertificatePassword` or `CertificateThumbprint`; encrypts the key ring),, `PublicBaseUrl` (email links; falls back to the obsolete `PatExpiryNotification:WebBaseUrl`), `RegistrationPageSize`, `UpstreamListingCacheSeconds` (default 300, per-feed override), `MirrorCacheRetentionDays` (default 0, per-feed override; mirrored versions not downloaded for that many days are removed by `RetentionCleaner`), `Cors` (`AllowedOrigins`, `AllowCredentials`), `SecurityHeaders` (`Enabled`, `EnableHsts`, `HstsMaxAgeDays`), `RequestRateLimit` (`Enabled`, `PermitLimit`, `WindowSeconds`, `QueueLimit`; off by default).
- `AllowPackageOverwrites`, `PackageDeletionBehavior`, `IsReadOnlyMode`, `MaxPackageSizeMiB` and `Retention` are only **defaults**. `Retention` is the default of NuGet feeds only: other feed types apply their own limits, through `ArtifactRetention` (the first three numbers of a version as major, minor and patch) in the hourly `RetentionCleanupService`, which also removes unused mirrored versions of every feed type (`ArtifactVersion.LastDownloadedAtUtc`, `Package.LastDownloadedAtUtc`). Per-feed values in the DB override them. Mirrors exist only per feed (`Feed.Mirrors`); the old global `Mirror` section is ignored with a startup warning.

Docker defaults (`Dockerfile`): the `/data` volume holds packages, symbols and the SQLite DB (`Data Source=/data/db/pagetto.db`).

## Code style (`.editorconfig`, warnings)

- 4-space indent (2-space for JSON).
- Naming: PascalCase for public members, `_camelCase` for private fields.
- `var` preferred. `System` usings first, all usings outside the namespace, file-scoped namespaces.
- Accessibility modifiers are required. Mark fields readonly where possible. No `this.`. Use predefined types (`int`, `string`).
- No primary constructors. No expression-bodied methods or constructors (properties and accessors are fine).
- One top-level type per file. The exception is a small helper that is only meaningful next to its owner (e.g. `enum PackageAddResult` beside `IPackageDatabase`). If a reader would look for the helper anywhere else, split it out.
- Suppress CS1591 for non-public XML docs.
- Logging goes through the `[LoggerMessage]` source generator: make the class `partial` and add `private partial void LogXxx(...)` methods at the bottom of the file. Don't call `ILogger.LogInformation`/`LogWarning`/… directly (CA1873). Keep placeholder names stable, since they are structured log properties (e.g. the `AUDIT` lines in `PackagePublishController`).
- Razor pages must not depend on the server culture (the Docker image is invariant). Render dates with `Html.DisplayDate(utc)` (`yyyy-MM-dd` with a UTC tooltip) and format numbers with the invariant culture (e.g. `RazorExtensions.ToMetric`).
- The build should stay free of warnings. EF migrations are exempt from CA1861 via `.editorconfig`.

## Testing

- The stack is xUnit + Moq. Integration tests use a temp-dir SQLite DB.
- Name the outer class after the type under test: `<Type>Tests` in `PaGetto.Core.Tests`, `<Type>Facts` in `PaGetto.Web.Tests`.
- Group each method's tests in a nested class named after the method, sharing a `FactsBase` (e.g. `PermissionServiceTests` → `public class CanPushAsync : FactsBase`). Don't use the old `The<Method>Method` naming.
- `PaGettoApplication` (`tests/PaGetto.Tests/Support/PaGettoApplication.cs`) is the `WebApplicationFactory` host. It pins `SystemTime` to 2020-01-01. Pass `inMemoryConfiguration: dict => …` to override config, and seed data with `AddPackageAsync`/`AddSymbolPackageAsync`.
- Adding a service index resource changes the expected counts in `PaGettoClientIntegrationTests`/`NuGetClientIntegrationTests` and the expected JSON in `TestData.resx`.

### Test environment (`testenv/`)

For manual checks, bug reproduction and UI testing, use the ready-made environment instead of building data by hand. See `testenv/README.md` and `docs/docs/Advanced/test-environment.md`.

```bash
docker compose -f testenv/docker-compose.yml up -d     # http://localhost:5000, latest image
docker compose -f testenv/docker-compose.yml down -v   # reset to the snapshot
PAGETTO_TAG=dev docker compose -f testenv/docker-compose.yml up -d   # your own image (docker build -t letreset/pagetto:dev .)
```

- It runs in `Local` auth mode with five feeds (Default mirrors nuget.org, Internal, Experimental, Archive read-only, and the Dart feed, which mirrors pub.dev), groups with different permissions, `Contoso.*` NuGet test packages and `contoso_*` Dart test packages (`testenv/packages/dart`). The test accounts and passwords are in `testenv/README.md`; they are test-only.
- `testenv/data` is the committed snapshot. It's copied into a Docker volume on the first start, so testing never changes the working tree. Don't commit changes to it unless you are regenerating it on purpose.
- To change the data set, edit `testenv/seed/seed.mjs` and regenerate the snapshot (steps in `testenv/README.md`).
- Push test packages from `testenv/packages` with Basic auth (username and password, or username and a personal access token from **My Tokens**).

### UI testing

- **Do everything in the web UI through the chrome-devtools MCP server** (`mcp__chrome-devtools__*`): navigating, clicking, filling forms, screenshots, console checks. Don't drive the UI with curl, headless scripts or other browser tools. If chrome-devtools isn't available, say so and stop instead of switching tools.
- Open one isolated browser context per account (`new_page` with `isolatedContext`), so sessions don't mix, and leave the user's own tabs alone. Close your pages when you are done.
- `testenv/UI-TESTS.md` is the UI regression checklist. Run it after UI changes and before a release, and report every check that fails and isn't marked **Known issue**. When a known issue is fixed, update its row; when you add or change UI behavior, add a row.

## Packages

All versions live in `Directory.Packages.props` (central package management). Never put versions in a `.csproj`.
