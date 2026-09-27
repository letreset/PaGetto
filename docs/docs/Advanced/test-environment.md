# Test environment

The repository has a ready-made test environment in [`testenv/`](https://github.com/letreset/PaGetto/tree/main/testenv): the latest `letreset/pagetto` image with feeds, accounts, groups, permissions and packages already set up. Use it to try a change, reproduce a bug or run the [UI checks](#ui-checks) without setting anything up by hand.

## Start it

You need Docker with Compose. From the repository root:

```bash
docker compose -f testenv/docker-compose.yml up -d
```

Open [http://localhost:5000](http://localhost:5000) and sign in with one of the [test accounts](#test-accounts).

The committed snapshot (`testenv/data`: the SQLite database and the package files) is mounted read-only and copied into a Docker volume on the first start, so nothing you do in the environment changes the working tree. To throw away all changes and start from the snapshot again:

```bash
docker compose -f testenv/docker-compose.yml down -v
```

| Variable | Default | Description |
|---|---|---|
| `PAGETTO_TAG` | `latest` | Image tag to test, e.g. `1.0.0` or a prerelease |
| `PAGETTO_PORT` | `5000` | Port on the host |
| `TESTENV_EMPTY` | `0` | `1` starts with an empty database instead of the snapshot |

The server runs in the ASP.NET Core `Development` environment: errors show a detailed error page instead of a bare status code, sign-in cookies also work over plain HTTP (for example when you open it through a LAN address instead of `localhost`), and HSTS is off. Production servers must not use `Development`.

To test your own build instead of a published image, build it with `docker build -t letreset/pagetto:dev .` and start the environment with `PAGETTO_TAG=dev`.

## Test accounts

These credentials exist only in the test data. Never use them for a real server.

| User | Password | Groups | Notes |
|---|---|---|---|
| `admin` | `Admin-Test-Password-1` | | Administrator |
| `alice` | `User-Test-Password-1` | Developers, Package owners | |
| `bob` | `User-Test-Password-1` | Developers | |
| `carol` | `User-Test-Password-1` | Developers | |
| `build-agent` | `User-Test-Password-1` | Build agents | Can't sign in to the web UI, only NuGet clients |

## What's in it

| Feed | Contents | Settings |
|---|---|---|
| Default | No packages | A mirror of nuget.org, so a restore or a package page pulls packages in (needs internet) |
| Internal | 22 `Contoso.*` packages, enough for two pages of results. Contoso.Logging has three versions (1.4.0 unlisted), symbol packages and a dependency on Contoso.Core | Overwrite policy **Prerelease only**, keep at most 3 major versions |
| Experimental | Prerelease packages (Contoso.Messaging 1.1.0-beta.2, Contoso.Preview.Ai) | Hard delete, keep at most 5 prerelease versions |
| Archive | Contoso.Legacy | Read-only |

Some versions have been downloaded a few times (Contoso.Core and Contoso.Logging the most), so download counts and the statistics page have data.

| Group | Members | Permissions |
|---|---|---|
| Developers | alice, bob, carol | Pull on Default, Internal and Archive; pull, push and delete on Experimental |
| Package owners | alice | Pull, push and delete on Internal |
| Build agents | build-agent | Pull on Default; pull and push on Internal and Experimental |

The packages are in `testenv/packages`, so you can push them again to try uploads. Local accounts authenticate with their username and password (Basic auth), for example:

```bash
curl -u carol:User-Test-Password-1 -X PUT -F package=@testenv/packages/Contoso.Mail.1.0.0.nupkg http://localhost:5000/feeds/experimental/api/v2/package
```

With the dotnet CLI, add the feed as a source with `--username` and `--password` first (see [Connecting a client](../feeds.md#connecting-a-client)).

## UI checks

[`testenv/UI-TESTS.md`](https://github.com/letreset/PaGetto/blob/main/testenv/UI-TESTS.md) lists the checks to run in the browser after UI changes and before a release, each with the account to use and the expected result. Checks that currently fail link to their issue, so a new failure stands out.

## Change the data set

The data set is built by `testenv/seed/seed.mjs`, which packs the test packages from `testenv/seed/template` and fills an empty server through the web UI forms and the NuGet API. Edit it, then regenerate the snapshot as described in [`testenv/README.md`](https://github.com/letreset/PaGetto/blob/main/testenv/README.md#regenerating-the-snapshot). Regenerate it too when a release changes the database schema in a way PaGetto can't migrate the snapshot from.
