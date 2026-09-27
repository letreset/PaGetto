# PaGetto.Protocol

PaGetto's client library for the [NuGet protocol](https://learn.microsoft.com/nuget/api/overview). PaGetto uses it for feed mirrors, and you can use it to talk to nuget.org or any other NuGet v3 server. See the [documentation](https://letreset.github.io/PaGetto/docs/Advanced/sdk) and the [samples](https://github.com/letreset/PaGetto/tree/main/samples).

## Getting started

```shell
dotnet add package PaGetto.Protocol
```

## Examples

### List Package Versions

Find all versions of the `Newtonsoft.Json` package:

```csharp
NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");

IReadOnlyList<NuGetVersion> versions = await client.ListPackageVersionsAsync("Newtonsoft.Json");

foreach (NuGetVersion version in versions)
{
    Console.WriteLine($"Found version: {version}");
}
```

### Download a package

```csharp
NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");

string packageId = "Newtonsoft.Json";
NuGetVersion packageVersion = new NuGetVersion("12.0.1");

using (Stream packageStream = await client.DownloadPackageAsync(packageId, packageVersion))
{
    Console.WriteLine($"Downloaded package {packageId} {packageVersion}");
}
```

### Find Package Metadata

```csharp
NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");

// Find the metadata for all versions of a package.
IReadOnlyList<PackageMetadata> items = await client.GetPackageMetadataAsync("Newtonsoft.Json");
if (!items.Any())
{
    Console.WriteLine($"Package 'Newtonsoft.Json' does not exist");
    return;
}

foreach (var metadata in items)
{
    Console.WriteLine($"Version: {metadata.Version}");
    Console.WriteLine($"Listed: {metadata.Listed}");
    Console.WriteLine($"Tags: {metadata.Tags}");
    Console.WriteLine($"Description: {metadata.Description}");
}

// Or, find the metadata for a single version of a package.
string packageId = "Newtonsoft.Json";
NuGetVersion packageVersion = new NuGetVersion("12.0.1");

PackageMetadata versionMetadata = await client.GetPackageMetadataAsync(packageId, packageVersion);

Console.WriteLine($"Listed: {versionMetadata.Listed}");
Console.WriteLine($"Tags: {versionMetadata.Tags}");
Console.WriteLine($"Description: {versionMetadata.Description}");
```

### Search for packages

Search for "json" packages:

```csharp
NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");
IReadOnlyList<SearchResult> results = await client.SearchAsync("json");

foreach (SearchResult result in results)
{
    Console.WriteLine($"Found package {result.PackageId} {result.Version}");
}
```
