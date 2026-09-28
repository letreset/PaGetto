# Debugging PaGetto

This page helps you find out why PaGetto, or a NuGet client talking to it, doesn't behave as expected.

## Read the logs

PaGetto logs to the console. With Docker, read them with `docker logs pagetto`; on Kubernetes, with `kubectl logs`.

By default only warnings, errors and a few important lines (startup, [audit lines](../configuration.md#audit-log), PAT expiry emails) are logged. To see more, raise the console log level with an environment variable:

```shell
Logging__Console__LogLevel__Default=Information
```

Use `Debug` for even more detail, and set it back when you are done: debug logging is verbose. You can also raise the level for one area only, for example to see the SQL that PaGetto sends to the database:

```shell
Logging__Console__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information
```

The same settings can go in the `Logging` section of `appsettings.json`. See [Logging in ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/logging/) for the format.

## PaGetto doesn't start

PaGetto checks its configuration on startup. When a value is missing or invalid, for example an unknown `Database:Type` or an incomplete `Authentication:Entra` section, it logs the problem and exits. Look for the first error in the log: it names the setting.

Remember the order in which configuration sources override each other: `appsettings.json`, user secrets (Development only), the [machine-wide config file](../configuration.md#machine-wide-config-file), environment variables, the command line and [secret files](../configuration.md#load-secrets-from-files). A value you changed in `appsettings.json` has no effect if an environment variable sets the same key.

If PaGetto can't reach its database, the log shows the connection error on the first migration or query. Check the connection string, the network path and the database user's permissions: PaGetto creates and updates its tables on startup.

## Check the server's health

| Path | What it tells you |
|---|---|
| `/livez` | The process is up |
| `/health` | The process is up and can reach its database. The JSON response lists each check |
| `/v3/index.json` | The NuGet service index. The URLs in it are the ones clients will use |

See [Health endpoint](../configuration.md#health-endpoint). The statistics page in the web UI shows which database and storage are in use, unless [disabled](../configuration.md#statistics).

## Debug a NuGet client

Most client problems show up in the service index. Open `https://your-server/v3/index.json` (or `https://your-server/feeds/{slug}/v3/index.json` for another [feed](../feeds.md)) in a browser and check that its URLs use the host name, scheme and path that clients see.

- **URLs use `http` or an internal host name behind a reverse proxy**: the proxy must send `X-Forwarded-Proto` and `X-Forwarded-Host`, which PaGetto reads.
- **URLs miss a path prefix**: set `PathBase`, see [Hosting on a different path](../configuration.md#hosting-on-a-different-path).
- **`401 Unauthorized`**: see [NuGet client returns 401 Unauthorized](../authentication.md#nuget-client-returns-401-unauthorized). The audit lines in the log show who was denied a push or delete, and why.
- **`403 Forbidden` on push or delete**: the credentials are valid, but the user has no push or delete [permission](../authentication.md#feed-permissions) on that feed. A user with no permission at all on the feed gets `404 Not Found`, as for a feed that doesn't exist. The audit lines in the log show the denied attempt.
- **`409 Conflict` on push**: that version already exists and the feed doesn't allow [overwrites](../configuration.md#enable-package-overwrites).
- **A large package fails to upload** (`413 Payload Too Large`): raise the [maximum package size](../configuration.md#maximum-package-size) of the server or the feed, and the request size limit of any reverse proxy in front of PaGetto.

To see every request a client makes, restore with detailed logging:

```shell
dotnet restore -v detailed
```

NuGet caches HTTP responses for 30 minutes. If a newly pushed version doesn't show up, clear that cache:

```shell
dotnet nuget locals http-cache --clear
```

## Debug a mirror

If a package isn't fetched from the upstream:

- Check that the feed has an **enabled** mirror on its settings page, see [Mirror (read-through cache)](../feeds.md#mirror-read-through-cache).
- Raise the log level to `Information` and restore again. Failing upstream calls are logged as warnings.
- A version newly published upstream can take up to the [upstream listing cache](../feeds.md#upstream-listing-cache) duration to appear. Set the feed's cache to `0` while you debug.

## Run PaGetto from source

To step through the code, clone the repository and run the `PaGetto` project:

```shell
git clone https://github.com/letreset/PaGetto.git
cd PaGetto
dotnet run --project src/PaGetto
```

This uses the `PaGetto` launch profile in `src/PaGetto/Properties/launchSettings.json`: the `Development` environment, `Debug` logging and `http://localhost:5095`. In Visual Studio or Rider, pick that profile and start debugging. Local runs use SQLite and store packages in the project folder, unless you change `src/PaGetto/appsettings.json` or use [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets).

Build and run the tests with:

```shell
dotnet build
dotnet test
```
