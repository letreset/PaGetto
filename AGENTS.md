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

A lightweight NuGet and symbol server: ASP.NET Core on .NET 10, implementing the NuGet v3 protocol, with pluggable database, storage and search backends.

This repo is **letreset/PaGetto**. Its main features:
- **Multi-feed**: each feed has its own packages, settings, mirror and permissions.
- **Local/Entra/Hybrid auth**: users, groups, PATs and per-feed permissions.
- **Admin UI**, PAT-expiry emails, and Data Protection keys persisted to storage.

## Branches, versions, releases

- `main` is the only long-lived branch. Work happens on `feature/*` branches.
- `main` is protected by a repository ruleset: no direct pushes, no merge commits (linear history), and changes land only through a pull request in letreset/PaGetto. The PR needs the required checks to pass (Build & test on ubuntu and windows, Docker build, Helm lint, CodeQL) and its review threads resolved; no approval is required.
- To ship a branch: rebase it on `origin/main`, push it, open a PR against `main`, and merge it with **Rebase and merge**, so each Conventional Commit stays separate for the git-cliff changelog. Use squash only for a branch that is really one change. Pushing `main` directly, or a `--no-ff` merge, is rejected.
- Versioning is semver starting at **1.0.0**.
- Pushing a `vX.Y.Z` tag on `main` runs `.github/workflows/release.yml`. It runs the tests, creates a GitHub release with a zip and a git-cliff changelog, pushes the Docker image `letreset/pagetto` to Docker Hub, pushes the Helm chart to `oci://ghcr.io/letreset/charts`, and pushes the `PaGetto.*` library packages (everything under `src/` except the host) to nuget.org through Trusted Publishing (repository variable `NUGET_USER`, no API key secret).
- A `-` in the tag (e.g. `v1.1.0-rc.1`) marks a prerelease, which does not move the `latest` image tag.
- Release notes credit contributors by GitHub username (`@nick`), never by real name. `cliff.toml` resolves authors through the GitHub API (`[remote.github]`, needs `GITHUB_TOKEN`); keep it that way, and use `@nick` when writing or editing release notes by hand.
- Use [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `ci:`, …). `cliff.toml` groups the changelog by these prefixes.
- The roadmap lives in GitHub issues on letreset/PaGetto, one issue per task.

## Repo layout

| Path | Contents |
|---|---|
| `src/PaGetto/` | Host: `Program.cs`, `Startup.cs` (DI and middleware pipeline), `ValidatePaGettoOptions`, `ConfigurePaGettoServer` (CORS, forms, forwarded headers, IIS), Data Protection key storage |
| `src/PaGetto.Core/` | Business logic, database-agnostic. `Authentication/`, `Configuration/`, `Content/`, `Email/`, `Entities/`, `Feeds/`, `Indexing/`, `Metadata/`, `Notifications/`, `Search/`, `ServiceIndex/`, `Statistics/`, `Storage/`, `Upstream/`, `Validation/` |
| `src/PaGetto.Web/` | Controllers, Razor Pages (`Pages/`, `Pages/Admin/`, `Pages/Account/`), `Middleware/`, `Authentication/`, routing (`PaGettoEndpointBuilder`, `Routes`), `PaGettoUrlGenerator` |
| `src/PaGetto.Protocol/` | NuGet v3 client and models, used for upstream mirrors |
| `src/PaGetto.Database.{Sqlite,SqlServer,PostgreSql,MySql}/` | EF Core context and migrations per provider |
| `src/PaGetto.{Aws,Azure,Gcp,Aliyun,Tencent}/` | Cloud storage providers |
| `tests/` | xUnit projects mirroring `src/` |
| `testenv/` | Docker Compose test environment with a seeded data snapshot, the seed script and the UI test checklist (`UI-TESTS.md`) |
| `docs/` | Docusaurus site, deployed to GitHub Pages by `docs.yml` |
| `deployment templates/` | Helm chart (`chart/pagetto`, built on bjw-s app-template) |

## Code navigation (CodeGraph)

The repo is indexed by CodeGraph (`.codegraph/` is local to each machine; only `.codegraph/.gitignore` is committed). When the index exists, use it before grep or reading whole files:

- **MCP tools:** `codegraph_explore` returns the current, line-numbered source of the symbols you name plus the call paths between them, including DI and interface-to-implementation hops that grep can't follow. `codegraph_callers` lists who calls a symbol, `codegraph_impact` what a change affects, `codegraph_files` the indexed file tree.
- **Always pass `projectPath`** (the repo root, e.g. `C:\Users\<you>\source\repos\PaGetto`). The server has no default project, and calls without it fail with "expected string, received undefined".
- Name symbols or files in the query, e.g. `"FeedResolutionMiddleware IFeedContext CurrentFeed"`. One call covers about six files; spend a second call on the uncovered area rather than reading files.
- The index lags behind uncommitted edits: files marked "changed on disk" come back without source, so read those directly, and re-read any file before editing it. Grep is still the tool for string literals, config keys, `.cshtml`, JSON and YAML.
- If the MCP server doesn't connect, the CLI (`codegraph explore "<symbols or question>"`) prints the same output when it is on the `PATH`; otherwise fall back to grep and reading.

## Build & test

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet run --project src/PaGetto     # http://localhost:5000
dotnet test --filter "FullyQualifiedName~UserServiceTests"
```

The SDK is pinned in `global.json`.

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
- Upstream clients come from `UpstreamClientFactory.CreateForFeed(feed)`: one enabled mirror gives a plain V2/V3 client, several give a `FallbackUpstreamClient` (merged version lists, download from the first mirror that has the package, failing mirrors skipped). Unless the feed's `UpstreamListingCacheSeconds` is 0, that client is wrapped in `CachingUpstreamClient`, which keeps non-empty version/metadata listings in the singleton `UpstreamListingCache`, keyed by feed id, `Feed.UpdatedAtUtc` (so saving the feed invalidates) and lowercased package id. Concurrent misses for a key share one upstream call, which runs with `CancellationToken.None` so one caller leaving doesn't cancel it for the others. `PackageService.MirrorAsync` serializes concurrent mirroring of the same `(feed, id, version)` through the singleton `PackageMirrorLock`.

### Authentication & authorization
- `Authentication:Mode` is one of `Config` (legacy `ApiKey`/`Credentials`, backward compatible), `Local`, `Entra` or `Hybrid`.
- The `NugetBasicAuth` scheme is the default. It forwards to the cookie scheme (`PaGetto.Auth`, 60-minute sliding expiry) when a cookie is present without an `Authorization` header, which separates browsers from client tools.
- `IFeedAuthenticationService` authenticates by PAT (`AuthenticateByTokenAsync`) or by username/password (`AuthenticateByCredentialsAsync`). Passwords use bcrypt; tokens are stored as prefix + hash.
- `FeedPermissionHandler` enforces per-feed permissions (pull/push/delete) for the current feed. User permissions come from groups via `PermissionService`, and `EntraRoleSyncService` syncs Entra app roles into local groups.

### Data model
All entities are defined in `Core/Entities/AbstractContext.cs`: Feed, FeedMirror, Package, PackageDependency, PackageType, TargetFramework, User, Group, UserGroup, FeedPermission, PersonalAccessToken.

### HTTP pipeline (`Startup.Configure`, in order)
ForwardedHeaders → PathBase → HSTS (optional) → `SecurityHeadersMiddleware` → ResponseCompression → `/livez` → `FeedStaticFilePathMiddleware` → StaticFiles → Authentication → RateLimiter (only when `RequestRateLimit:Enabled`) → `FeedResolutionMiddleware` → Routing → Authorization → CORS → `OperationCancelledMiddleware` (maps `OperationCanceledException` to 409) → endpoints → health check (`HealthCheck:Path`).

### API routes (`PaGettoEndpointBuilder`; every route is also available under `/feeds/{slug}/`)
- `GET /v3/index.json`: the service index.
- `GET /v3/search`, `GET /v3/autocomplete`.
- `GET /v3/registration/{id}/index.json`, `…/page/{lower}/{upper}.json`, `…/{version}.json`. The index is paged once a package has more than `RegistrationPageSize` versions (default 64).
- `GET /v3/package/{id}/index.json`, and `…/{version}/{id}.{version}.nupkg`, `.nuspec`, `/readme`, `/icon`.
- `GET /v3/dependents`.
- `PUT /api/v2/package`, `DELETE`/`POST` `/api/v2/package/{id}/{version}`.
- `PUT /api/v2/symbol`, `GET /api/download/symbols/…`.

## Configuration

`PaGettoOptions` (`Core/Configuration/`) is bound from the config root. Sources, later ones winning: `appsettings*.json` (from `PAGETTO_CONFIG_ROOT` if set), user secrets, the optional machine-wide file (`%ProgramData%\PaGetto\appsettings.json` or `/etc/pagetto/appsettings.json`, see `Program.AddPlatformConfigFile`), environment variables, command line, `/run/secrets` (key-per-file). `ValidatePaGettoOptions` fails startup on invalid values.

Main keys:
- `Database`, `Storage`, `Search`: each has a `Type`. `Database:ServerVersion` (MySQL only, optional) skips server version detection, which otherwise runs once per connection string (`MySqlServerVersionResolver`).
- `Authentication`: `Mode`, `Entra`, `InitialAdmin` (`Username`, `Password`; `InitialAdminSeeder` creates this local admin at startup, after migrations, while no admin exists in `Local`/`Hybrid`), token and lockout limits.
- `Email`, `PatExpiryNotification`.
- `MaxPackageSizeGiB`, `RegistrationPageSize`, `UpstreamListingCacheSeconds` (default 300, per-feed override), `Cors` (`AllowedOrigins`, `AllowCredentials`), `SecurityHeaders` (`Enabled`, `EnableHsts`, `HstsMaxAgeDays`), `RequestRateLimit` (`Enabled`, `PermitLimit`, `WindowSeconds`, `QueueLimit`; off by default).
- `HealthCheck`, `Statistics`.
- `Mirror`, `AllowPackageOverwrites`, `PackageDeletionBehavior` and `Retention` are only **defaults and seeds**. Per-feed values in the DB override them. The global `Mirror` block is `[Obsolete]` and is only read once, to seed the default feed.

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

- It runs in `Local` auth mode with four feeds (Default mirrors nuget.org, Internal, Experimental, Archive read-only), groups with different permissions and `Contoso.*` test packages. The test accounts and passwords are in `testenv/README.md`; they are test-only.
- `testenv/data` is the committed snapshot. It's copied into a Docker volume on the first start, so testing never changes the working tree. Don't commit changes to it unless you are regenerating it on purpose.
- To change the data set, edit `testenv/seed/seed.mjs` and regenerate the snapshot (steps in `testenv/README.md`).
- Push test packages from `testenv/packages` with Basic auth (username and password, or username and a personal access token from **My Tokens**).

### UI testing

- **Do everything in the web UI through the chrome-devtools MCP server** (`mcp__chrome-devtools__*`): navigating, clicking, filling forms, screenshots, console checks. Don't drive the UI with curl, headless scripts or other browser tools. If chrome-devtools isn't available, say so and stop instead of switching tools.
- Open one isolated browser context per account (`new_page` with `isolatedContext`), so sessions don't mix, and leave the user's own tabs alone. Close your pages when you are done.
- `testenv/UI-TESTS.md` is the UI regression checklist. Run it after UI changes and before a release, and report every check that fails and isn't marked **Known issue**. When a known issue is fixed, update its row; when you add or change UI behavior, add a row.

## Packages

All versions live in `Directory.Packages.props` (central package management). Never put versions in a `.csproj`.
