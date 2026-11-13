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

A lightweight package server: ASP.NET Core on .NET 10, with pluggable database, storage and search backends. It started as a NuGet and symbol server (the NuGet v3 protocol) and also hosts Dart and Flutter packages (the pub protocol), npm packages (the npm registry API), Maven packages (the Maven repository layout), Python packages (PyPI's simple repository API), Rust crates (Cargo's sparse registry protocol) and Go modules (the GOPROXY protocol). Each feed serves one ecosystem.

This repo is **letreset/PaGetto**. Its main features:
- **Multi-feed**: each feed has its own packages, settings, mirror and permissions.
- **Feed types**: a feed is a NuGet feed, a Dart (pub) feed, an npm feed, a Maven feed, a Python (PyPI) feed, a Rust (Cargo) feed or a Go feed. More ecosystems can be added the same way (see "Feed types and ecosystems").
- **Local/Entra/Hybrid auth**: users, groups, PATs and per-feed permissions.
- **Admin UI**, PAT-expiry emails, and Data Protection keys persisted to storage.

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

## Repo layout

| Path | Contents |
|---|---|
| `src/PaGetto/` | Host: `Program.cs`, `Startup.cs` (DI and middleware pipeline), `ValidatePaGettoOptions`, `ConfigurePaGettoServer` (CORS, forms, forwarded headers, IIS), Data Protection key storage |
| `src/PaGetto.Core/` | Business logic, database-agnostic. `Artifacts/` (packages of non-NuGet feeds), `Authentication/`, `Configuration/`, `Content/`, `Email/`, `Entities/`, `Feeds/`, `Indexing/`, `Maven/`, `Metadata/`, `Notifications/`, `Npm/`, `Pub/` (Dart), `Search/`, `ServiceIndex/`, `Statistics/`, `Storage/`, `Upstream/`, `Validation/` |
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

### Feed types and ecosystems
- `Feed.Type` (`FeedType`: `NuGet` = 0, `Pub` = 1, `Npm` = 2, `Maven` = 3, `PyPI` = 4, `Cargo` = 5, `Go` = 6) is set when a feed is created and never changes. The values are stored in the database. The default feed is always a NuGet feed.
- **NuGet** keeps its own model (`Package` and its child tables, `IPackageDatabase`, `IPackageStorageService`, search providers). Don't put other ecosystems into those tables: NuGet version normalization drops build metadata, which other ecosystems treat as part of the version.
- **Every other ecosystem** stores its packages as artifacts: `Artifact` (feed, normalized name) and `ArtifactVersion` (version, lowercased `NormalizedVersion`, `Listed`, `Description`, the manifest as `MetadataJson`, SHA-256 `ContentHash`, `Size`, `Downloads`, `HasReadme`, `CachedFrom`, `Published`) `ArtifactTag` (a named pointer to a version, e.g. an npm dist-tag; removed with its version) and `ArtifactFile` (one file of a version that has several, e.g. a Maven POM and JAR or the wheels and sdist of a Python version, with its SHA-1, MD5, SHA-256 and SHA-512; removed with its version). `Core/Artifacts/` has what they share: `IArtifactDatabase` (reads are untracked), `ArtifactFileContent` (a file read into a temporary file with its checksums), `IArtifactStorageService` (`packages/{feedSlug}/{name}/{version}/archive` and `/readme`, and `/files/{fileName}`; a `:` in the name is a folder) and `ArtifactVersionOrder` (version order per feed type).
- **Dart** is in `Core/Pub/`: `PubVersion` (pub's version order, build metadata included), `PubArchiveReader` and `PubspecYaml` (reads the gzipped tar archive and converts `pubspec.yaml` to JSON; the tar is read by `PubTarReader`, because `System.Formats.Tar` rejects the headers the pub client writes), `PubPackageService` (list, download with mirroring, publish) and `PubUpstreamClient` (mirrors of pub servers; the first mirror that has a package serves it, listings go through `UpstreamListingCache`).
- **npm** is in `Core/Npm/`: `NpmVersion` (semver precedence), `NpmPackageName` (scoped names, which arrive as `@scope%2fname`, are stored lowercased), `NpmArchiveReader` (the tarball's `package.json` and readme, read with `PubTarReader`), `NpmTarballHashes` (SHA-512 `integrity`, SHA-1 `shasum`), `NpmPackageService` (the package document, publish, deprecate, unpublish, dist-tags, mirroring) and `NpmUpstreamClient`. A deprecated version is unlisted, with the message in its manifest. The registry is at `npm/` under the feed so names can't collide with web UI pages. Tarball URLs are built by `NpmUrlHelperExtensions.NpmTarball`, not by routing, because routing lowercases URLs.
- **Maven** is in `Core/Maven/`: `MavenVersion` (Maven's `ComparableVersion` order; alpha, beta, milestone, rc and snapshot are prereleases), `MavenPath` and `MavenFileName` (the repository layout: a file of a version, the metadata of an artifact or of a snapshot, and their checksums), `MavenPomReader` (the POM as the version's manifest), `MavenMetadataXml` (writes `maven-metadata.xml` from what is stored, so its checksums are stable), `MavenRepositoryService` (download with mirroring, upload) and `MavenUpstreamClient`. A package is `groupId:artifactId`, stored lowercased; its files are `ArtifactFile` rows. The server computes the checksums and the metadata; uploaded checksums are checked, uploaded metadata is ignored. Snapshot files can always be replaced, and snapshots are not mirrored. The repository is at `maven2/` under the feed.
- **Python** is in `Core/PyPI/`: `PyPIVersion` (PEP 440 normalization and order), `PyPIPackageName` (PEP 503 normalization; names are stored normalized), `PyPIFileName` (wheel and sdist names, and the version in them), `PyPIArchiveReader` and `PyPICoreMetadata` (a wheel's `METADATA` or an sdist's `PKG-INFO`, read into the PEP 566 JSON form as the version's manifest, without the description, which is the readme), `PyPIPackageService` (project pages, download with mirroring, upload) and `PyPIUpstreamClient` (mirrors that speak the simple API, JSON or HTML; the first mirror that has a project lists its files). Versions are stored normalized. A version's wheels and sdists are `ArtifactFile` rows, and each wheel's core metadata is stored next to it as a `{wheel}.metadata` row (PEP 658), which listings and the web UI leave out. An unlisted version is yanked. The index is at `pypi/` under the feed.
- **Rust** is in `Core/Cargo/`: `CargoVersion` (semver in Cargo's order; versions that differ only in build metadata count as one when publishing), `CargoPackageName` (crates.io's name rules and the index file path; names are stored lowercased, the published case is in the manifest), `CargoPublishRequest` (the length-prefixed body of `cargo publish`), `CargoCrateReader` (checks `{name}-{version}/Cargo.toml` in the `.crate`, reads the readme, with `PubTarReader`), `CargoIndexEntry` (the manifest is an index line without `cksum` and `yanked`, plus the `Cargo.toml` fields the web UI shows; it writes the index lines), `CargoPackageService` (index, download with mirroring, publish, yank) and `CargoUpstreamClient` (mirrors that are sparse registries; the first mirror that has a crate lists its versions, downloads follow the mirror's `config.json`). An unlisted version is yanked. The registry is at `cargo/` under the feed.
- **Go** is in `Core/Go/`: `GoVersion` (canonical semver with a `v`, `+incompatible`, pseudo-versions as prereleases), `GoModulePath` (the go command's path rules, the `/vN` major version check, and the `!` escaping of uppercase letters; a module is stored under its escaped path, which is unique regardless of case, and versions are matched by exact case), `GoProxyPath` (the GOPROXY request of a path), `GoModuleZipReader` (checks the `{module}@{version}/` layout, reads `go.mod` and `README.md`, and drops the folder entries `git archive` writes, which break `go mod verify`), `GoModFile` (the module, Go version and requirements of a `go.mod`), `GoModuleManifest` (the manifest: the module path, the version's time and its `go.mod`, from which `.info` and `.mod` are answered), `GoModuleService` (list, latest, info, mod, zip with mirroring, upload) and `GoUpstreamClient` (mirrors that are module proxies; the first that lists a module lists its versions, `.info` and `.mod` of versions the feed lacks are passed through, a version is stored when its zip is first downloaded). An unlisted version is left out of the list and `@latest`. The proxy is at `go/` under the feed.
- A controller or Razor page model that belongs to one ecosystem carries `[FeedType(...)]`. `FeedTypeMiddleware` (after authorization) answers 404 when it is requested on a feed of another type. With user accounts it lets anonymous requests through to the endpoint's own challenge, so the feed type can't be probed. `NugetBasicAuthenticationHandler` picks the challenge from the endpoint: `Bearer realm="pub"` for pub endpoints, `Basic realm="PaGetto"` for Maven, PyPI and Go endpoints (sbt credentials name the realm), `Cargo login_url="…/connect"` for Cargo endpoints, `Basic realm="NuGet Server"` otherwise.
- The web UI: `Pages/Index` lists artifacts on non-NuGet feeds, `Pages/Artifact` (`/artifacts/{id}/{version?}`) is their package page, `Pages/Shared/_ConnectPub`, `_ConnectNpm`, `_ConnectMaven`, `_ConnectPyPI`, `_ConnectCargo` and `_ConnectGo` their Connect pages. `Pages/Package` and `Pages/Upload` are NuGet only. `FeedTypeNames` has the display names and client URLs.
- **To add an ecosystem** (e.g. RubyGems): add a `FeedType` value, a `Core/<Name>/` folder with its version rules, archive reader and package service on top of the artifact services, a case in `ArtifactVersionOrder` and `FeedTypeNames`, a controller with `[FeedType]` and its routes in `PaGettoEndpointBuilder`, and the ecosystem's parts of `Pages/Artifact` and Connect. No migration is needed unless the artifact tables lack something.

### Authentication & authorization
- `Authentication:Mode` is required (startup fails without it) and is one of `Legacy` (config-file `Authentication:ApiKeys`/`Credentials`, formerly `Config`, which is still accepted; the old top-level `ApiKey` fails validation), `Local`, `Entra` or `Hybrid`.
- The `NugetBasicAuth` scheme is the default. It forwards to the cookie scheme (`PaGetto.Auth`, sliding expiry of `Authentication:SessionTimeoutMinutes`, default 60) when a cookie is present without an `Authorization` header, which separates browsers from client tools.
- `IFeedAuthenticationService` authenticates by PAT (`AuthenticateByTokenAsync`) or by username/password (`AuthenticateByCredentialsAsync`). Passwords use bcrypt; tokens are stored as prefix + hash. A PAT is accepted in the `X-NuGet-ApiKey` header, as the Basic password, as a Bearer token (what `dart pub` and the npm clients send), or as the whole `Authorization` header when it starts with the token prefix (what Cargo sends). With a PAT as the Basic password, the username must be the token's owner or `__token__` (`FeedAuthenticationService.TokenUsername`, what Python clients send).
- `FeedPermissionHandler` enforces per-feed permissions (pull/push/delete) for the current feed. User permissions come from groups via `PermissionService`, and `EntraRoleSyncService` syncs Entra app roles into local groups.

### Data model
All entities are defined in `Core/Entities/AbstractContext.cs`: Feed, FeedMirror, Package, PackageDependency, PackageType, TargetFramework, Artifact, ArtifactVersion, ArtifactTag, User, Group, UserGroup, FeedPermission, PersonalAccessToken, AuditEvent.

### HTTP pipeline (`Startup.Configure`, in order)
ForwardedHeaders → PathBase → HSTS (optional) → `SecurityHeadersMiddleware` → ResponseCompression → `/livez` → `FeedStaticFilePathMiddleware` → StaticFiles → Authentication → RateLimiter (only when `RequestRateLimit:Enabled`) → `FeedResolutionMiddleware` → Routing → Authorization → `FeedTypeMiddleware` → CORS → `OperationCancelledMiddleware` (maps `OperationCanceledException` to 409) → endpoints → health check (`HealthCheck:Path`).

### API routes (`PaGettoEndpointBuilder`; every route is also available under `/feeds/{slug}/`)
- `GET /v3/index.json`: the service index.
- `GET /v3/search`, `GET /v3/autocomplete`.
- `GET /v3/registration/{id}/index.json`, `…/page/{lower}/{upper}.json`, `…/{version}.json`. The index is paged once a package has more than `RegistrationPageSize` versions (default 64).
- `GET /v3/package/{id}/index.json`, and `…/{version}/{id}.{version}.nupkg`, `.nuspec`, `/readme`, `/icon`.
- `GET /v3/dependents`.
- `PUT /api/v2/package`, `DELETE`/`POST` `/api/v2/package/{id}/{version}`.
- `PUT /api/v2/symbol`, `GET /api/download/symbols/…`.

The routes above answer on NuGet feeds. Dart feeds have the pub API ([repository spec v2](https://github.com/dart-lang/pub/blob/master/doc/repository-spec-v2.md), `PubPackageController`):
- `GET /api/packages/{name}`, and the deprecated `GET /api/packages/{name}/versions/{version}`.
- `GET /packages/{name}/versions/{version}.tar.gz`: the archive.
- Publishing, in three steps: `GET /api/packages/versions/new`, `POST /api/packages/versions/newUpload` (multipart, part `file`; publishes the package and answers 204 with a `Location`), `GET /api/packages/versions/newUploadFinish` (reports the outcome, which is the only server message the client shows).

npm feeds have the npm registry API under `npm/` (`NpmPackageController`). A scoped name is `@scope%2fname` in one segment or `@scope/name` in two:
- `GET npm/{name}` (the package document; `?write=true` leaves out mirror-only versions), `GET npm/{name}/-/{name}-{version}.tgz`.
- `PUT npm/{name}`: publish (with `_attachments`) or a changed document (deprecate; versions left out are unpublished). `PUT npm/{name}/-rev/{rev}` is the same, `DELETE npm/{name}/-rev/{rev}` unpublishes the package, and `DELETE npm/{**path}` takes the tarball delete of `npm unpublish`.
- `GET npm/-/package/{name}/dist-tags`, `PUT`/`DELETE npm/-/package/{name}/dist-tags/{tag}`, `GET npm/-/v1/search`, `GET npm/-/ping`, `GET npm/-/whoami`.

Maven feeds have the [repository layout](https://maven.apache.org/repositories/layout.html) under `maven2/` (`MavenRepositoryController`):
- `GET`/`HEAD maven2/{**path}`: a file of a version (`{groupPath}/{artifactId}/{version}/{fileName}`, with `X-Checksum-SHA1`/`-MD5`/`-SHA256` headers), its `.sha1`/`.md5`/`.sha256`/`.sha512`, or a `maven-metadata.xml` of an artifact or snapshot version.
- `PUT maven2/{**path}`: uploads a file (answers 201), checks an uploaded checksum, ignores uploaded metadata. Errors carry their message as the reason phrase, which Maven and Gradle show.

Python feeds have the [simple repository API](https://packaging.python.org/en/latest/specifications/simple-repository-api/) and the legacy upload API under `pypi/` (`PyPIPackageController`):
- `GET pypi/simple/` and `GET pypi/simple/{name}/`: the projects and a project's files, as HTML (PEP 503) or JSON (PEP 691, API 1.1) by the `Accept` header. A name that is not normalized redirects (301). File URLs are absolute and built by `PyPIUrlHelperExtensions`.
- `GET`/`HEAD pypi/files/{name}/{version}/{fileName}`: a wheel or sdist, or with `.metadata` the core metadata of a wheel.
- `POST pypi/legacy/`: an upload of one file (multipart, `:action=file_upload`, the file as `content`); answers 200, or 409 for an existing file. Errors carry their message as the reason phrase, which twine shows.

Cargo feeds have the [sparse index](https://doc.rust-lang.org/cargo/reference/registry-index.html) and the [registry web API](https://doc.rust-lang.org/cargo/reference/registry-web-api.html) under `cargo/` (`CargoRegistryController`); clients use `sparse+https://server/feeds/{slug}/cargo/index/`:
- `GET cargo/index/config.json`: `dl`, `api` and `auth-required` (false only for anonymous callers of a Legacy server without credentials). URLs are built by `CargoUrlHelperExtensions`.
- `GET cargo/index/{**path}`: a crate's index file at `1/a`, `2/ab`, `3/a/abc` or `ab/cd/abcd` of the lowercased name, one JSON line per version; any other path is 404.
- `GET cargo/api/v1/crates/{name}/{version}/download`: the `.crate` file. `GET cargo/api/v1/crates?q=` searches (`cargo search`).
- `PUT cargo/api/v1/crates/new`: publishes (push permission). `DELETE .../{name}/{version}/yank` and `PUT .../unyank` unlist and relist (delete permission). Errors are `{"errors":[{"detail":…}]}`, which Cargo shows.

Go feeds have the [GOPROXY protocol](https://go.dev/ref/mod#goproxy-protocol) under `go/` (`GoProxyController`); clients use `GOPROXY=https://server/feeds/{slug}/go`. Module paths and versions are escaped (`!` and the lowercase letter for an uppercase one):
- `GET go/{module}/@v/list` (versions without pseudo-versions; empty for a module with only pseudo-versions), `GET go/{module}/@latest`, `GET go/{module}/@v/{version}.info`, `.mod` and `.zip`. Unknown modules and versions are 404 with a plain text message, which makes the go command try the next proxy. URLs are built by `GoUrlHelperExtensions`.
- `PUT go/{module}/@v/{version}.zip`: uploads a module zip (push permission); answers 201, or 409 for an existing version. Errors are plain text.

## Configuration

`PaGettoOptions` (`Core/Configuration/`) is bound from the config root. Sources, later ones winning: `appsettings*.json` (from `PAGETTO_CONFIG_ROOT` if set), user secrets, the optional machine-wide file (`%ProgramData%\PaGetto\appsettings.json` or `/etc/pagetto/appsettings.json`, see `Program.AddPlatformConfigFile`), environment variables, command line, `/run/secrets` (key-per-file). `ValidatePaGettoOptions` fails startup on invalid values.

Main keys:
- `Database`, `Storage`, `Search`: each has a `Type`. `Database:ServerVersion` (MySQL only, optional) skips server version detection, which otherwise runs once per connection string (`MySqlServerVersionResolver`).
- `Authentication`: `Mode`, `Entra`, token and lockout limits, `SessionTimeoutMinutes` (cookie, default 60) and `MinPasswordLength` (default 12, 8 to 72). In `Local`/`Hybrid`, `InitialAdminSeeder` creates the local admin `admin`/`admin` at startup, after migrations, while no admin exists; `User.MustChangePassword` keeps it on `/Account/ChangePassword` (`MustChangePasswordMiddleware`) and out of basic auth until the password is changed.
- `Email`, `PatExpiryNotification`.
- `MaxPackageSizeMiB` (default 8192; the legacy `MaxPackageSizeGiB` is still read via `PaGettoOptions.EffectiveMaxPackageSizeMiB`), `ForwardedHeaders` (`KnownProxies`, `KnownNetworks`; empty trusts every client), `DataProtection` (`CertificatePath`+`CertificatePassword` or `CertificateThumbprint`; encrypts the key ring),, `PublicBaseUrl` (email links; falls back to the obsolete `PatExpiryNotification:WebBaseUrl`), `RegistrationPageSize`, `UpstreamListingCacheSeconds` (default 300, per-feed override), `Cors` (`AllowedOrigins`, `AllowCredentials`), `SecurityHeaders` (`Enabled`, `EnableHsts`, `HstsMaxAgeDays`), `RequestRateLimit` (`Enabled`, `PermitLimit`, `WindowSeconds`, `QueueLimit`; off by default).
- `HealthCheck`, `Statistics`.
- `AllowPackageOverwrites`, `PackageDeletionBehavior`, `IsReadOnlyMode`, `MaxPackageSizeMiB` and `Retention` are only **defaults**. Per-feed values in the DB override them. Mirrors exist only per feed (`Feed.Mirrors`); the old global `Mirror` section is ignored with a startup warning.

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
