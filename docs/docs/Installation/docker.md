# Run PaGetto on Docker

The image is [`letreset/pagetto`](https://hub.docker.com/r/letreset/pagetto) on Docker Hub, built for `linux/amd64` and `linux/arm64`.

## Quick start

```shell
docker run -d --name pagetto -p 5000:8080 -v pagetto-data:/data \
  letreset/pagetto:latest
```

The image runs with local accounts (`Authentication:Mode` is `Local`). Open [`http://localhost:5000/`](http://localhost:5000/) and sign in as `admin` with the password `admin`: PaGetto asks for a new password first. Then create a token under **My Tokens** and push a package with it:

```shell
dotnet nuget push -s http://localhost:5000/v3/index.json -k <token> MyPackage.1.0.0.nupkg
```

:::warning

Change the default `admin` password right after the first start, before others can reach the server.

To run without accounts, like BaGetter, set `Authentication__Mode=Legacy` and `Authentication__ApiKeys__0__Key` to a long random value. Without an API key, anyone who can reach the server can push packages.

:::

## Image tags

| Tag | Meaning |
|-----|---------|
| `latest` | Latest stable release |
| `1`, `1.0` | Latest release in that major or minor line |
| `1.0.0` | Exact version, never changes |
| `1.1.0-rc.1` | Prerelease, never tagged `latest` |

Pin an exact version (or at least a major version) in production, and upgrade on purpose. The [releases page](https://github.com/letreset/PaGetto/releases) lists the changes in each version.

## Build your own image

Build the image from a clone of the repository:

```shell
docker build -t pagetto:local .
```

The build restores NuGet packages from `https://api.nuget.org/v3/index.json`. If the build machine can't reach nuget.org, point the restore at another feed (for example a mirror, or a PaGetto feed that mirrors nuget.org) with the `NuGetSource` build argument:

```shell
docker build -t pagetto:local --build-arg NuGetSource=https://nuget.example.com/v3/index.json .
```

That feed must serve every package the build needs. `NuGetSource` replaces the sources from `nuget.config` instead of adding to them.

## The `/data` volume

By default the image keeps all of its state in `/data`:

| Path | Contents |
|---|---|
| `/data/packages/…` | Packages, per feed |
| `/data/symbols/…` | Symbol files, per feed |
| `/data/db/pagetto.db` | The SQLite database |
| `/data/dataprotection/keyring.xml` | Data Protection keys (sign-in cookies, forms) |

Mount a named volume or a host folder there, or you lose everything when the container is recreated. These defaults come from environment variables in the image (`Storage__Path=/data`, `Database__Type=Sqlite`, `Database__ConnectionString=Data Source=/data/db/pagetto.db`, `Search__Type=Database`); override them to use another database or cloud storage.

## Configure PaGetto

Configure PaGetto with environment variables, using `__` (two underscores) as the section separator, for example `Database__Type` for `Database:Type`. You can pass them with `-e`, with an `--env-file`, or in a compose file. For the full list, see [Configuration](../configuration.md).

Secrets can also be mounted as files under `/run/secrets`, one file per setting (see [Load secrets from files](../configuration.md#load-secrets-from-files)).

## Docker Compose

A production-like setup with PostgreSQL:

```yaml
services:
  pagetto:
    image: letreset/pagetto:2
    restart: unless-stopped
    ports:
      - "5000:8080"
    environment:
      Database__Type: PostgreSql
      Database__ConnectionString: Host=db;Database=pagetto;Username=pagetto;Password=${POSTGRES_PASSWORD}
      Search__Type: Database
      Authentication__Mode: Legacy
    secrets:
      - source: pagetto_api_key
        target: Authentication__ApiKeys__0__Key
    volumes:
      - pagetto-data:/data
    depends_on:
      - db

  db:
    image: postgres:17
    restart: unless-stopped
    environment:
      POSTGRES_DB: pagetto
      POSTGRES_USER: pagetto
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - postgres-data:/var/lib/postgresql/data

volumes:
  pagetto-data:
  postgres-data:

secrets:
  pagetto_api_key:
    file: ./secrets/api-key.txt
```

Put `POSTGRES_PASSWORD=…` in a `.env` file next to `compose.yaml`, and the API key in `secrets/api-key.txt`, then run `docker compose up -d`. For user accounts and Entra ID sign-in, add the settings from [Authentication](../authentication.md#docker-compose-example).

## Health checks

| Endpoint | Checks |
|---|---|
| `/livez` | Only that the process is up. Use it for liveness probes. |
| `/health` | The database (not the package storage). Use it for readiness probes and monitoring. The path is set by `HealthCheck:Path`. |

## Restore packages

Use this package source:

`http://localhost:5000/v3/index.json`

Other [feeds](../feeds.md) are at `http://localhost:5000/feeds/{slug}/v3/index.json`. Some helpful guides:

- [Visual Studio](https://docs.microsoft.com/en-us/nuget/consume-packages/install-use-packages-visual-studio#package-sources)
- [NuGet.config](https://docs.microsoft.com/en-us/nuget/reference/nuget-config-file#package-source-sections)

## Symbol server

Publish a [symbol package](https://docs.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg) the same way as a package:

```shell
dotnet nuget push -s http://localhost:5000/v3/index.json -k change-me MyPackage.1.0.0.snupkg
```

Load symbols from this symbol location:

`http://localhost:5000/api/download/symbols`

For Visual Studio, see [Configure symbol locations](https://learn.microsoft.com/en-us/visualstudio/debugger/specify-symbol-dot-pdb-and-source-files-in-the-visual-studio-debugger#configure-symbol-locations-and-loading-options).

## Running PaGetto behind a reverse proxy

Run PaGetto behind a reverse proxy to add HTTPS and your own domain. For the API to return correct URLs, the proxy must forward the `Host` header (or `X-Forwarded-Host`) and `X-Forwarded-Proto`. For more information, see the [ASP.NET Core documentation](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer).

Consider binding the port to localhost only (`-p 127.0.0.1:5000:8080`), so unencrypted traffic never leaves the machine.

### Apache 2 configuration

```config
<IfModule mod_ssl.c>
	<VirtualHost *:443>

		ServerName nuget.example.com

		ProxyRequests Off
		ProxyPreserveHost On
		ProxyPass / http://localhost:5000/
		ProxyPassReverse / http://localhost:5000/
		RequestHeader set X-Forwarded-Proto https

		SSLCertificateFile /etc/letsencrypt/live/nuget.example.com/fullchain.pem #managed by certbot
		SSLCertificateKeyFile /etc/letsencrypt/live/nuget.example.com/privkey.pem #managed by certbot
		Include /etc/letsencrypt/options-ssl-apache.conf
		Header always set Content-Security-Policy upgrade-insecure-requests
	</VirtualHost>
</IfModule>
```

To send HSTS from PaGetto itself instead of the proxy, see [Security headers](../configuration.md#security-headers).
