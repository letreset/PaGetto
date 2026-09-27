# Azure Artifacts

[Azure Artifacts](https://learn.microsoft.com/en-us/azure/devops/artifacts/) is the package feature of Azure DevOps. It hosts NuGet, npm, Maven, Python, Cargo and Universal packages in feeds scoped to an Azure DevOps organization or project.

## PaGetto vs Azure Artifacts

| | PaGetto | Azure Artifacts |
|---|---|---|
| License and cost | Open source (MIT), free. You pay only for the infrastructure you run it on | Part of Azure DevOps. A small amount of storage is free, more is billed |
| Hosting | Self-hosted: Docker, Kubernetes (Helm), IIS, any ASP.NET Core 10 host, on any cloud or on-premises | Microsoft-hosted service, or Azure DevOps Server on-premises |
| Package formats | NuGet packages and symbols | NuGet, npm, Maven, Python, Cargo, Universal packages |
| Separate package sources | [Feeds](../feeds.md), each with its own settings and permissions | Organization- and project-scoped feeds, with views (`@local`, `@prerelease`, `@release`) |
| Proxy of nuget.org | Per-feed [mirrors](../feeds.md#mirror-read-through-cache): nuget.org or any NuGet v3/v2 feed, with basic, bearer or custom-header authentication | Upstream sources: public registries and other Azure Artifacts feeds |
| Authentication | Local accounts, Microsoft Entra ID (with app roles synced to groups), personal access tokens | Azure DevOps identities (Microsoft Entra ID or Microsoft accounts), PATs, the Azure Artifacts credential provider |
| Permissions | Pull, push and delete per feed, per group | Feed roles (reader, collaborator, contributor, owner) |
| Overwriting a version | Configurable per feed: never, prerelease only, or always | Not allowed. A version number can't be reused, even after deletion |
| Retention | Per-feed version-based [auto-deletion](../configuration.md#package-auto-deletion) | Per-feed limit on the number of versions kept |
| Symbol server | Built in (`.snupkg`) | Azure DevOps symbol server |
| Where your data lives | Your database and your storage account or disk | Microsoft's cloud (or your Azure DevOps Server) |
| Internet access needed | No. It can run fully offline, with mirrors used when reachable | Yes, for the hosted service |

## Which one to pick

- Pick **Azure Artifacts** if your team already lives in Azure DevOps, needs formats other than NuGet, or doesn't want to run a server.
- Pick **PaGetto** if you want to control where packages are stored, run in an isolated or on-premises network, avoid per-user or storage billing, or use GitHub, GitLab or another CI system instead of Azure DevOps.

:::note

Details about Azure Artifacts can change. Check the [Azure Artifacts documentation](https://learn.microsoft.com/en-us/azure/devops/artifacts/) for current features and pricing.

:::
