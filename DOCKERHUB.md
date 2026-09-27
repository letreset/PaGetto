# PaGetto

A lightweight, self-hosted **NuGet and symbol server** for .NET teams. It implements the NuGet v3 protocol, so `dotnet`, NuGet, Visual Studio and Rider work with it out of the box.

It supports:

- **Multiple feeds**: each feed has its own packages, settings, retention, and read-through mirror of nuget.org or any other feed.
- **Users and permissions**: local accounts, **Microsoft Entra ID** sign-in, groups, per-feed pull/push/delete permissions, and personal access tokens with expiry email reminders.
- **An admin UI** for feeds, accounts and groups.
- **Pluggable backends**: SQLite, SQL Server, PostgreSQL or MySQL for the database; the file system, Azure Blob, AWS S3, Google Cloud Storage, Aliyun OSS or Tencent COS for storage.

## Quick start

```bash
docker run -d --name pagetto -p 5000:8080 -v pagetto-data:/data \
  -e ApiKey=change-me \
  letreset/pagetto:latest
```

Open http://localhost:5000, then push a package with the API key:

```bash
dotnet nuget push -s http://localhost:5000/v3/index.json -k change-me MyPackage.1.0.0.nupkg
```

Without `ApiKey`, anyone who can reach the server can push. By default the image stores packages, symbols, the SQLite database and Data Protection keys in `/data`. Mount a volume there to keep them.

## Configuration

Configure PaGetto with environment variables, using `__` as the section separator. For example, to use PostgreSQL:

```bash
docker run -d -p 5000:8080 -v pagetto-data:/data \
  -e ApiKey=change-me \
  -e Database__Type=PostgreSql \
  -e Database__ConnectionString="Host=db;Database=pagetto;Username=pagetto;Password=..." \
  letreset/pagetto:latest
```

Secrets can also be mounted as files under `/run/secrets` (key-per-file). User accounts, groups and Entra ID sign-in are enabled with `Authentication__Mode` (`Config`, `Local`, `Entra` or `Hybrid`). In `Local` mode, set `Authentication__InitialAdmin__Username` and `Authentication__InitialAdmin__Password` (ideally as a secret) to create the first administrator on startup; see the [authentication docs](https://letreset.github.io/PaGetto/docs/authentication) and the full [configuration docs](https://letreset.github.io/PaGetto/docs/configuration).

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
