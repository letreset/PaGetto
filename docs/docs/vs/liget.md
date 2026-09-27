# LiGet

[LiGet](https://github.com/ai-traders/liget) is an open-source NuGet server created with a Linux-first approach. It started as a v2 server with strong [Paket](https://fsprojects.github.io/Paket/) support. It hasn't seen active development for several years, so check its repository for its current state before choosing it.

## PaGetto vs LiGet

| | PaGetto | LiGet |
|---|---|---|
| Status | Actively maintained, regular releases | No recent releases |
| Runtime | .NET 10 | Older .NET Core versions |
| NuGet protocol | v3 (push and delete on the standard `api/v2/package` endpoints) | v2 first, with strong Paket support |
| Metadata storage | A database: SQLite, SQL Server, PostgreSQL or MySQL | Local files |
| Package storage | File system, Azure Blob, AWS S3, Google Cloud Storage, Aliyun OSS, Tencent COS | Local file system |
| Separate package sources | [Feeds](../feeds.md), each with its own settings and permissions | A single feed |
| Proxy of nuget.org | Per-feed [mirrors](../feeds.md#mirror-read-through-cache), several upstreams per feed | Caching proxy of nuget.org |
| Authentication | Local accounts, Microsoft Entra ID, groups, per-feed permissions, personal access tokens | Minimal |
| Symbol server | Built in (`.snupkg`) | No |
| Web UI | Package browser and admin UI | No |
| Deployment | Docker (`amd64`, `arm64`), Kubernetes (Helm), IIS, cloud providers | Docker |

## Which one to pick

- Pick **LiGet** only if you depend on a v2-specific feature it offers and can live with an unmaintained server.
- Pick **PaGetto** for everything else. Paket works with NuGet v3 feeds, so it can use PaGetto too.
