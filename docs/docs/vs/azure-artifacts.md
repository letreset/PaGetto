---
sidebar_position: 2
---

# Azure Artifacts

[Azure Artifacts](https://learn.microsoft.com/en-us/azure/devops/artifacts/) is the package feature of Azure DevOps. It hosts NuGet, npm, Maven, Python, Cargo and Universal Packages in feeds scoped to an Azure DevOps organization or project. PaGetto is a self-hosted, open-source package server for 15 package formats, one per [feed](../feeds.md).

## PaGetto vs Azure Artifacts

| | PaGetto | Azure Artifacts |
|---|---|---|
| License and cost | Open source (MIT), free. You pay only for the infrastructure you run it on | Part of Azure DevOps. 2 GiB of storage per organization are free, more is billed |
| Hosting | Self-hosted: Docker, Kubernetes (Helm), IIS, any ASP.NET Core 10 host, on any cloud or on-premises | Microsoft-hosted service, or Azure DevOps Server on-premises |
| Package formats | 15 package formats, NuGet with a symbol server (see [below](#package-formats)) | NuGet, npm, Maven (and Gradle), Python, Cargo, Universal Packages |
| Separate package sources | [Feeds](../feeds.md) of one format each, with their own settings, mirrors and permissions | Organization- and project-scoped feeds, each holding every format, with views (`@Local`, `@Prerelease`, `@Release`) to promote versions |
| Proxy of public registries | Per-feed [mirrors](../feeds.md#mirror-read-through-cache) for every format (nuget.org, npmjs, Maven Central, PyPI, crates.io, ...), or any other server of that format, several per feed in order, with optional upstream authentication | Upstream sources: the public registries of its formats (nuget.org, npmjs.com, Maven Central, PyPI and others) and other Azure Artifacts feeds. Other registries only for npm |
| Authentication | Local accounts, Microsoft Entra ID (with app roles synced to groups), personal access tokens that can be limited to read-only or to one feed | Azure DevOps identities (Microsoft Entra ID or Microsoft accounts), PATs, the Azure Artifacts credential provider |
| Permissions | Pull, push and delete per feed, granted to groups | Feed roles (Feed Reader, Feed and Upstream Reader, Feed Publisher, Feed Owner), and views shared separately |
| Overwriting a version | Per feed: never, prerelease only, or always | Not allowed. A version number can't be reused, even after deletion |
| Retention | Per-feed version-based [auto-deletion](../configuration.md#package-auto-deletion) for every format, and removal of [unused mirrored versions](../configuration.md#unused-mirrored-packages) | Per feed: a maximum number of versions per package, combined with a number of days without download. Versions promoted to a view are kept |
| Symbol server | Built in for NuGet feeds (`.snupkg`) | Azure DevOps symbol server |
| Where your data lives | Your database and your storage account or disk | Microsoft's cloud (or your Azure DevOps Server) |
| Internet access needed | No. It can run fully offline, with mirrors used when reachable | Yes, for the hosted service |

## Package formats

| Format | PaGetto | Azure Artifacts |
|---|---|---|
| [NuGet](../nuget.md) | Yes, with symbols (`.snupkg`) | Yes |
| [npm](../npm.md) | Yes | Yes |
| [Maven](../maven.md) | Yes | Yes |
| [Python (PyPI)](../pypi.md) | Yes | Yes |
| [Rust (Cargo)](../cargo.md) | Yes | Yes |
| [Dart and Flutter (pub)](../dart.md), [Go modules](../go.md), [Ruby gems](../rubygems.md), [Hex](../hex.md), [PHP (Composer)](../composer.md), [CocoaPods](../cocoapods.md), [Swift](../swift.md), [R (CRAN)](../cran.md), [Ivy (sbt)](../ivy.md), [Visual Studio extensions (VSIX)](../vsix.md) | Yes | No |
| Universal Packages (arbitrary files, with the Azure CLI) | No | Yes |

## Which one to pick

- Pick **Azure Artifacts** if your team already lives in Azure DevOps, its formats cover what you need, and you don't want to run a server.
- Pick **PaGetto** if you need one of the formats Azure Artifacts lacks (such as Go, Ruby, PHP or Dart), want to control where packages are stored, run in an isolated or on-premises network, avoid per-user or storage billing, or use GitHub, GitLab or another CI system instead of Azure DevOps.

:::note

Details about Azure Artifacts can change. Check the [Azure Artifacts documentation](https://learn.microsoft.com/en-us/azure/devops/artifacts/) for current features and pricing.

:::
