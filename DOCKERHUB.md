# PaGetto

A lightweight, self-hosted **package server** for 16 ecosystems. Each feed hosts one package format and speaks its protocol, so the standard clients work with nothing more than the feed's URL and a token, and every feed can mirror the public registry of its ecosystem.

## Package formats

| Languages | Package format | Clients | Mirror of |
|---|---|---|---|
| C#, F#, Visual Basic (.NET) | [NuGet](https://letreset.github.io/PaGetto/docs/nuget), with symbols (`.snupkg`) | dotnet CLI, NuGet CLI, Visual Studio, Rider, Paket | nuget.org, any NuGet v3 or v2 feed |
| JavaScript, TypeScript | [npm](https://letreset.github.io/PaGetto/docs/npm) | npm, Yarn, pnpm | registry.npmjs.org |
| Java, Kotlin, Scala, Groovy | [Maven](https://letreset.github.io/PaGetto/docs/maven) | Maven, Gradle, sbt | Maven Central |
| Python | [PyPI](https://letreset.github.io/PaGetto/docs/pypi) (simple API) | pip, uv, Poetry, twine | PyPI |
| Rust | [Cargo](https://letreset.github.io/PaGetto/docs/cargo) (sparse registry) | Cargo | crates.io |
| Go | [Go modules](https://letreset.github.io/PaGetto/docs/go) (GOPROXY) | go command | proxy.golang.org |
| Ruby | [RubyGems](https://letreset.github.io/PaGetto/docs/rubygems) (compact index) | Bundler, gem | rubygems.org |
| PHP | [Composer](https://letreset.github.io/PaGetto/docs/composer) | Composer | Packagist |
| Dart, Flutter | [pub](https://letreset.github.io/PaGetto/docs/dart) | `dart pub`, `flutter pub` | pub.dev |
| Elixir, Erlang | [Hex](https://letreset.github.io/PaGetto/docs/hex) | Mix, Rebar3 | hex.pm |
| Swift, Objective-C | [CocoaPods](https://letreset.github.io/PaGetto/docs/cocoapods) (CDN spec repository) | CocoaPods | cdn.cocoapods.org |
| Swift | [Swift package registry](https://letreset.github.io/PaGetto/docs/swift) | Swift Package Manager, Xcode | other Swift package registries |
| R | [CRAN](https://letreset.github.io/PaGetto/docs/cran) (source packages) | `install.packages`, renv | CRAN |
| Scala (sbt) | [Ivy](https://letreset.github.io/PaGetto/docs/ivy) (sbt's Ivy layout) | sbt | the sbt plugin repository |
| Visual Studio extensions | [VSIX](https://letreset.github.io/PaGetto/docs/vsix) (private extension gallery) | Visual Studio | other extension galleries |
| VS Code extensions | [VS Code](https://letreset.github.io/PaGetto/docs/vscode) (extension gallery API) | VSCodium, code-server, Code - OSS | Open VSX |

## Features

- **Multiple feeds**: each feed serves one package format under `/feeds/{slug}/`, with its own packages, settings, retention, permissions, webhook and read-through mirrors (several upstreams per feed, in order).
- **Users and permissions**: local accounts, **Microsoft Entra ID** sign-in, groups, per-feed pull/push/delete permissions, and personal access tokens (also limited to reading or to one feed) with expiry email reminders.
- **A web UI** to browse every feed, with readmes and install commands per client, and admin pages for feeds, accounts and groups.
- **Operations**: retention per feed, webhooks, an audit log, health checks and OpenTelemetry metrics.
- **Pluggable backends**: SQLite, SQL Server, PostgreSQL or MySQL for the database; the file system, Azure Blob, AWS S3, Google Cloud Storage, Aliyun OSS or Tencent COS for storage.

## Quick start

```bash
docker run -d --name pagetto -p 5000:8080 -v pagetto-data:/data \
  letreset/pagetto:latest
```

Open http://localhost:5000 and sign in as `admin` with the password `admin`. PaGetto asks for a new password first. Then create a token under **My Tokens** and push a package with it. The default feed is a NuGet feed:

```bash
dotnet nuget push -s http://localhost:5000/v3/index.json -k <token> MyPackage.1.0.0.nupkg
```

For the other formats, create a feed of their type under **Admin > Feeds** and point the client at it, for example an npm feed with the slug `js`:

```bash
npm publish --registry http://localhost:5000/feeds/js/npm/
```

The feed's **Connect** page shows the configuration and authentication for its clients.

Change the default password right after the first start. By default the image stores packages, symbols, the SQLite database and Data Protection keys in `/data`. Mount a volume there to keep them.

## Configuration

Configure PaGetto with environment variables, using `__` as the section separator. For example, to use PostgreSQL:

```bash
docker run -d -p 5000:8080 -v pagetto-data:/data \
  -e Database__Type=PostgreSql \
  -e Database__ConnectionString="Host=db;Database=pagetto;Username=pagetto;Password=..." \
  letreset/pagetto:latest
```

Secrets can also be mounted as files under `/run/secrets` (key-per-file). User accounts, groups, LDAP or Active Directory users and single sign-on (Entra ID, any OpenID Connect provider or a SAML 2.0 identity provider) are enabled with `Authentication__Mode` (required: `Local`, `Entra`, `Oidc`, `Saml`, `Hybrid` or `Legacy`). In `Local` mode PaGetto creates the administrator `admin` with the password `admin` on the first start and asks for a new password on the first sign-in; see the [authentication docs](https://letreset.github.io/PaGetto/docs/authentication) and the full [configuration docs](https://letreset.github.io/PaGetto/docs/configuration).

## Tags

| Tag | Meaning |
|-----|---------|
| `latest` | Latest stable release |
| `1`, `1.0` | Latest release in that major/minor line |
| `1.0.0` | Exact version (immutable) |
| `1.1.0-rc.1` | Prerelease (never tagged `latest`) |

Images are published for `linux/amd64` and `linux/arm64`.

## Kubernetes

```bash
helm install pagetto oci://ghcr.io/letreset/charts/pagetto --version <version>
```

## Links

- Source & issues: https://github.com/letreset/PaGetto
- Releases & changelog: https://github.com/letreset/PaGetto/releases
- Documentation: https://letreset.github.io/PaGetto/
