# Import packages from a local feed

[Local feeds](https://docs.microsoft.com/en-us/nuget/hosting-packages/local-feeds) let you use a folder as a NuGet package source.

:::info

Please refer to the [PaGetto vs local feeds](../vs/local-feeds.md) page for reasons to upgrade to PaGetto.

:::

## Steps

Make sure that you've installed [nuget.exe](https://www.nuget.org/downloads). In PowerShell, run:

```powershell
$source = "C:\path\to\local\feed"
$destination = "http://localhost:5000/v3/index.json"
```

If you've [configured PaGetto to require an API Key](../configuration.md#require-an-api-key), set it using [the `setapikey` command](https://docs.microsoft.com/en-us/nuget/reference/cli-reference/cli-ref-setapikey):

```powershell
& nuget.exe setapikey "MY-API-KEY" -Source $destination
```

With user accounts (the `Local`, `Entra` or `Hybrid` [authentication modes](../authentication.md)), the API key settings are ignored. Use a [personal access token](../authentication.md#personal-access-tokens-pats) of a user with push permission on the target feed as the API key in `setapikey`, or add the destination as a source with a username and password. To import into a feed other than the default one, use its URL, `http://localhost:5000/feeds/{slug}/v3/index.json`, as `$destination`.

Now run the following PowerShell script:

```powershell
$packages = nuget list -AllVersions -Source $source

$packages | % {
  $id, $version = $_ -Split " "
  $nupkg = $id + "." + $version + ".nupkg"
  $path = [IO.Path]::Combine($source, $id, $version, $nupkg)

  Write-Host "nuget.exe push -Source $destination ""$path"""
  & nuget.exe push -Source $destination $path
}
```
