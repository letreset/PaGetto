# NuGet.Server

[NuGet.Server](https://github.com/NuGet/NuGet.Server) is a lightweight, standalone NuGet server for Windows and IIS. We strongly recommend moving to PaGetto if you use NuGet.Server. Feel free to open a [GitHub issue](https://github.com/letreset/PaGetto/issues) if you need help migrating.

## PaGetto vs NuGet.Server

| | PaGetto | NuGet.Server |
|---|---|---|
| Runtime | .NET 10, cross-platform | .NET Framework (ASP.NET), Windows only |
| Hosting | Docker (`amd64`, `arm64`), Kubernetes (Helm), IIS, any ASP.NET Core 10 host | IIS |
| NuGet protocol | v3 | v2 only (OData) |
| Metadata storage | A database: SQLite, SQL Server, PostgreSQL or MySQL | Package files on disk, with an in-memory cache |
| Package storage | File system, Azure Blob, AWS S3, Google Cloud Storage, Aliyun OSS, Tencent COS | Local file system |
| Scaling | Several replicas can share a database and cloud storage | A single server, slows down as the package count grows |
| Separate package sources | [Feeds](../feeds.md), each with its own settings and permissions | One feed per site |
| Proxy of nuget.org | Per-feed [mirrors](../feeds.md#mirror-read-through-cache) | No |
| Authentication | Local accounts, Microsoft Entra ID, groups, per-feed permissions, personal access tokens | A single API key for pushes, plus whatever IIS provides |
| Retention | Per-feed version-based [auto-deletion](../configuration.md#package-auto-deletion) | No |
| Symbol server | Built in (`.snupkg`) | No |
| Web UI | Package browser and admin UI | No |
| Maintenance | Actively maintained | Rarely updated |

## Migration guide

You can use the [NuGet.Server migration](../Import/nugetserver.md) guide to import your NuGet.Server packages into PaGetto.
