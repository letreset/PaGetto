# NuGet packages

A feed of the type **NuGet** is a NuGet v3 feed with a symbol server: the dotnet CLI, the NuGet CLI, Visual Studio, Rider and Paket restore packages from it and push packages and symbol packages to it, and it can mirror [nuget.org](https://www.nuget.org) or any other NuGet v3 or v2 feed.

The default feed is always a NuGet feed, so a server that was set up before it had other feeds keeps its URLs.

## Create a NuGet feed

The default feed (slug `default`) exists from the first start. Create more on **Admin > Feeds** with **New feed** and the type **NuGet** (see [Managing feeds](feeds.md#managing-feeds)). Each feed has its own service index:

| Feed | Service index |
|---|---|
| Default feed | `https://your-server/v3/index.json` |
| Any other feed, e.g. `internal` | `https://your-server/feeds/internal/v3/index.json` |

All endpoints (search, registrations, package content, push, delete, symbols) are found through the service index, so a client only needs its URL. The feed's **Connect** page in the web UI shows it with the commands for the current authentication mode.

## Configure the client

With the dotnet CLI:

```shell
dotnet nuget add source "https://your-server/feeds/internal/v3/index.json" --name internal \
    --username "<your-username>" --password "<your-personal-access-token>"
```

Or in a `nuget.config` next to your solution:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="internal" value="https://your-server/feeds/internal/v3/index.json" />
    <add key="mirror" value="https://your-server/v3/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <internal>
      <add key="Username" value="build-agent" />
      <add key="ClearTextPassword" value="%PAGETTO_TOKEN%" />
    </internal>
  </packageSourceCredentials>
</configuration>
```

Keep secrets out of source control: reference an environment variable as above, or add the credentials with `dotnet nuget update source … --username … --password …` on each machine.

With the NuGet CLI, `NuGet Sources Add` stores the password encrypted in `%AppData%\NuGet\NuGet.config`; add `-StorePasswordInClearText` where encrypted passwords don't work (for example on Linux and macOS):

```shell
NuGet Sources Add -Name "internal" -Source "https://your-server/feeds/internal/v3/index.json" -UserName "<your-username>" -Password "<your-personal-access-token>"
```

For Visual Studio, see [package sources](https://learn.microsoft.com/nuget/consume-packages/install-use-packages-visual-studio#package-sources); for the file format, see [nuget.config](https://learn.microsoft.com/nuget/reference/nuget-config-file#package-source-sections). Visual Studio (with NuGet 6.13 or later) also shows package readmes, which the service index advertises (`ReadmeUriTemplate`).

### Authentication

NuGet clients send a username and password (HTTP Basic) for restores, and an API key for pushes. What PaGetto accepts depends on the [authentication mode](authentication.md#authentication-modes):

| Mode | Restore (username / password) | Push (`-k` API key) |
|---|---|---|
| `Legacy` | A `Credentials` entry, if any are [configured](configuration.md#private-feeds) | An [`ApiKeys`](configuration.md#require-an-api-key) value |
| `Local` | Your username and a PAT (recommended), or your account password | A PAT |
| `Entra` | Your username and a PAT as the password | A PAT |
| `Hybrid` | Your username and a PAT, or a local account's password | A PAT |

Prefer a [personal access token](authentication.md#personal-access-tokens-pats) over the account password on build agents and developer machines: it can expire, be revoked on its own, and failed PAT attempts never lock the account. When a PAT is the password, the username must be the token's owner (see [Using PaGetto from package clients](authentication.md#using-pagetto-from-package-clients)).

## Push a package

```shell
dotnet nuget push -s https://your-server/feeds/internal/v3/index.json -k <your-personal-access-token> MyPackage.1.0.0.nupkg
```

Pushes follow the feed's [settings](feeds.md#feed-settings): its overwrite policy, size limit and read-only mode. Users with push permission can also upload `.nupkg` and `.snupkg` files on the feed's **Upload** page in the [web UI](web-ui.md#upload).

`dotnet nuget delete` unlists the version by default, or deletes it when the feed's **Deletion behavior** is hard delete (see [hard deletions](configuration.md#enable-package-hard-deletions)). Relist an unlisted version on its package page.

## Symbols

Push a [symbol package](https://learn.microsoft.com/nuget/create-packages/symbol-packages-snupkg) (`.snupkg`) the same way as a package, after the package itself:

```shell
dotnet nuget push -s https://your-server/feeds/internal/v3/index.json -k <your-personal-access-token> MyPackage.1.0.0.snupkg
```

Debuggers load symbols from the feed's symbol location:

| Feed | Symbol location |
|---|---|
| Default feed | `https://your-server/api/download/symbols` |
| Any other feed, e.g. `internal` | `https://your-server/feeds/internal/api/download/symbols` |

For Visual Studio, see [Configure symbol locations](https://learn.microsoft.com/visualstudio/debugger/specify-symbol-dot-pdb-and-source-files-in-the-visual-studio-debugger#configure-symbol-locations-and-loading-options). Symbol files are stored next to the packages, under `symbols/{feed}/…`.

## Mirror nuget.org

Add a mirror with the source `https://api.nuget.org/v3/index.json` on the feed's settings page. A NuGet feed can mirror any NuGet v3 feed, or a v2 feed with **Use NuGet V2 (legacy) protocol**, and several of them in order, for example nuget.org and a vendor's private feed. Developers then add only the PaGetto feed to `nuget.config`, and the vendor credentials stay on the server. See [Mirror (read-through cache)](feeds.md#mirror-read-through-cache) and [Import nuget.org packages](Import/nugetorg.md).

## In the web UI

A NuGet feed's package list has filters for the package type (dependencies, .NET tools, .NET templates), the target framework and tags, and its package pages show install snippets for the .NET CLI, the Package Manager console, `PackageReference`, Central Package Management, Paket, scripts and Cake, the target frameworks, dependencies per framework, and the packages of the feed that use it. Every package also has an [Atom feed](web-ui.md#atom-feed) of its versions. See [Web UI](web-ui.md).

The search of the web UI and of NuGet clients (`dotnet package search`, the Visual Studio browse tab) give the same results.

## Feed settings and limits

All [feed settings](feeds.md#feed-settings) apply to NuGet feeds. Two settings apply only to them:

| | NuGet feeds |
|---|---|
| Deletion behavior | Whether `dotnet nuget delete` unlists or deletes. See [hard deletions](configuration.md#enable-package-hard-deletions) |
| Registration page size | Packages with more versions than `RegistrationPageSize` get a paged registration index. See [registration page size](configuration.md#registration-page-size) |

NuGet normalizes versions as NuGet clients do: build metadata (`+abc`) is not part of the version, so `1.0.0+1` and `1.0.0+2` are one version.

The APIs of other feed types answer `404 Not Found` on a NuGet feed, and the NuGet API answers `404 Not Found` on feeds of other types.

## More

- [Import nuget.org packages](Import/nugetorg.md), and packages from [NuGet.Server](Import/nugetserver.md) or a [local feed](Import/local-feeds.md)
- [PaGetto SDK](Advanced/sdk.md): the NuGet v3 client library PaGetto uses for its mirrors
