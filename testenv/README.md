# Test environment

A ready-made PaGetto instance for manual and agent testing: the latest Docker image with feeds, accounts, groups, permissions and packages already set up.

```bash
docker compose -f testenv/docker-compose.yml up -d
```

Open http://localhost:5000 and sign in with one of the test accounts below. `docker compose -f testenv/docker-compose.yml down -v` stops it and throws away every change, so the next `up` starts from the snapshot again.

The full guide, including what's in the data set, is in [docs/docs/Advanced/test-environment.md](../docs/docs/Advanced/test-environment.md). The UI checks to run after changes are in [UI-TESTS.md](UI-TESTS.md).

## Test accounts

These credentials only exist in this test data. Never use them anywhere else.

| User | Password | Access |
|---|---|---|
| `admin` | `Admin-Test-Password-1` | Administrator |
| `alice` | `User-Test-Password-1` | Developers, Package owners |
| `bob` | `User-Test-Password-1` | Developers |
| `carol` | `User-Test-Password-1` | Developers |
| `build-agent` | `User-Test-Password-1` | Build agents, no web sign-in |

## Layout

| Path | Contents |
|---|---|
| `docker-compose.yml` | The environment. `PAGETTO_TAG` picks the image tag (default `latest`), `PAGETTO_PORT` the port (default 5000) |
| `data/` | The snapshot: `db/pagetto.db` and the package files. Mounted read-only and copied into a Docker volume on the first start |
| `packages/` | The test packages (`Contoso.*`), built by the seed script |
| `seed/seed.mjs` | Builds the packages and fills an empty server with the data set |
| `seed/template/` | The project the test packages are packed from |
| `UI-TESTS.md` | The UI test checklist |

## Regenerating the snapshot

Do this when the data set changes, or when a new release changes the database schema in a way the snapshot can't be migrated from. Needs Docker, Node 18+ and the .NET SDK.

```bash
docker compose -f testenv/docker-compose.yml down -v
TESTENV_EMPTY=1 docker compose -f testenv/docker-compose.yml up -d
node testenv/seed/seed.mjs
docker compose -f testenv/docker-compose.yml stop
rm -rf testenv/data && mkdir testenv/data
docker compose -f testenv/docker-compose.yml cp pagetto:/data/. testenv/data/
rm -rf testenv/data/dataprotection
docker compose -f testenv/docker-compose.yml down -v
```

`TESTENV_EMPTY=1` starts without the snapshot, and PaGetto creates the `admin` account from `Authentication:InitialAdmin`. The Data Protection keys are left out of the snapshot on purpose, so every environment creates its own. Delete a file in `packages/` to have the seed script pack it again.
