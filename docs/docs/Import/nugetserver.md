# Import NuGet.Server packages

[NuGet.Server](https://github.com/NuGet/NuGet.Server) is a lightweight standalone NuGet server. It is strongly recommended that you upgrade to PaGetto if you use NuGet.Server. Feel free to open a [GitHub issue](https://github.com/letreset/PaGetto/issues) if you need help migrating.

:::info

Please refer to the [PaGetto vs NuGet.Server](../vs/nugetserver.md) page for reasons to upgrade to PaGetto.

:::

## Steps

Make sure that you've installed [nuget.exe](https://www.nuget.org/downloads). In PowerShell, run:

```powershell
$source = "<NuGet.Server package source>"
$destination = "<PaGetto package source>"
```

If you've [configured PaGetto to require an API Key](../configuration.md#require-an-api-key), set it using [the `setapikey` command](https://docs.microsoft.com/en-us/nuget/reference/cli-reference/cli-ref-setapikey):

```powershell
& nuget.exe setapikey "MY-API-KEY" -Source $destination
```

With user accounts (the `Local`, `Entra` or `Hybrid` [authentication modes](../authentication.md)), the API key settings are ignored. Use a [personal access token](../authentication.md#personal-access-tokens-pats) of a user with push permission on the target feed as the API key in `setapikey`, or add the destination as a source with a username and password. To import into a feed other than the default one, use its URL, `http://localhost:5000/feeds/{slug}/v3/index.json`, as `$destination`.

Now run the following PowerShell script:

```powershell
if (!(Test-Path "Web.config")) {
  throw "Please run this script in the same directory as NuGet.Server's Web.config file"
}

(& nuget.exe list -AllVersions -Source $source).Split([Environment]::NewLine) | % {
  $id = $_.Split(" ")[0].Trim()
  $version = $_.Split(" ")[1].Trim()

  $path = [IO.Path]::Combine("Packages", $id, $version, "${id}.${version}.nupkg")

  Write-Host "nuget.exe push -Source $destination ""$path"""
  & nuget.exe push -Source $destination $path
}
```
