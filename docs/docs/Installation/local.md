# Run PaGetto on your computer

Each [release](https://github.com/letreset/PaGetto/releases) has a zip that runs anywhere .NET runs: Windows, Linux and macOS.

## Install

1. Install the [ASP.NET Core 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (the "ASP.NET Core Runtime", or the .NET SDK, which includes it).
2. Download `pagetto-<version>.zip` and `SHA256SUMS.txt` from the [releases page](https://github.com/letreset/PaGetto/releases).
3. Verify the download:

   ```shell
   # Linux and macOS
   sha256sum --check --ignore-missing SHA256SUMS.txt
   ```

   ```powershell
   # Windows (PowerShell): compare the output with the line in SHA256SUMS.txt
   (Get-FileHash .\pagetto-1.0.0.zip -Algorithm SHA256).Hash.ToLower()
   ```

4. Extract the zip and start PaGetto from that folder:

   ```shell
   dotnet PaGetto.dll
   ```

5. Browse to [`http://localhost:5000/`](http://localhost:5000/).

To listen on another address or port, pass `--urls`, for example `dotnet PaGetto.dll --urls http://0.0.0.0:8080`.

## Configure PaGetto

Edit `appsettings.json` in the extracted folder, or set environment variables (`Database__Type`, `ApiKey`, …). By default PaGetto uses SQLite (`pagetto.db`) and stores packages in the directory you start it from. Set `Storage:Path` to an absolute path to keep them somewhere else. For the full list of settings, see [Configuration](../configuration.md).

:::warning

Secure your server by requiring an API key to publish packages. See [Require an API key](../configuration.md#require-an-api-key), or set up [user accounts](../authentication.md).

:::

To keep your settings out of the app folder, put them in the [machine-wide config file](../configuration.md#machine-wide-config-file) instead: `%ProgramData%\PaGetto\appsettings.json` on Windows, `/etc/pagetto/appsettings.json` on Linux and macOS. This is the easiest option for Windows services and systemd units.

When you upgrade, keep your `appsettings.json`, the database and the package folder, and replace the rest of the files. See [Upgrading](../upgrading.md).

To run PaGetto as a Windows service behind IIS, see [Windows IIS proxy](iis-proxy.md).

## Publish packages

```shell
dotnet nuget push -s http://localhost:5000/v3/index.json -k <api-key> package.1.0.0.nupkg
```

Publish a [symbol package](https://docs.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg) the same way:

```shell
dotnet nuget push -s http://localhost:5000/v3/index.json -k <api-key> symbol.package.1.0.0.snupkg
```

## Restore packages

Use this package source:

`http://localhost:5000/v3/index.json`

Other [feeds](../feeds.md) are at `http://localhost:5000/feeds/{slug}/v3/index.json`. Some helpful guides:

- [Visual Studio](https://docs.microsoft.com/en-us/nuget/consume-packages/install-use-packages-visual-studio#package-sources)
- [NuGet.config](https://docs.microsoft.com/en-us/nuget/reference/nuget-config-file#package-source-sections)

## Symbol server

Load symbols from this symbol location:

`http://localhost:5000/api/download/symbols`

For Visual Studio, see [Configure symbol locations](https://learn.microsoft.com/en-us/visualstudio/debugger/specify-symbol-dot-pdb-and-source-files-in-the-visual-studio-debugger#configure-symbol-locations-and-loading-options).
