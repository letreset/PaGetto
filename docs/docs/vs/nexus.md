# Nexus

[Sonatype Nexus Repository](https://www.sonatype.com/products/sonatype-nexus-repository) is a universal repository manager. It hosts packages for many ecosystems (NuGet, Maven, npm, Docker, PyPI and more) and comes as a free edition with limits and a commercial Pro edition.

## PaGetto vs Nexus

| | PaGetto | Nexus Repository |
|---|---|---|
| License | Open source (MIT), free, no usage limits | Free Community Edition with limits, commercial Pro edition |
| Package formats | NuGet packages and symbols | NuGet plus most other package formats and Docker images |
| Hosting | Self-hosted: Docker, Kubernetes (Helm), IIS, any ASP.NET Core 10 host | Self-hosted, or Sonatype cloud for Pro |
| NuGet protocol | v3 (push and delete on the standard `api/v2/package` endpoints) | v2 and v3 |
| Symbol server | Built in (`.snupkg`) | Check the Sonatype documentation for your version |
| Separate package sources | [Feeds](../feeds.md), each with its own settings and permissions | Hosted, proxy and group repositories |
| Proxy of nuget.org | Per-feed [mirrors](../feeds.md#mirror-read-through-cache), several upstreams per feed | Proxy repositories, combined with group repositories |
| Authentication | Local accounts, Microsoft Entra ID, groups, personal access tokens | Local users, LDAP, API keys; SAML and more in Pro |
| Retention | Per-feed version-based [auto-deletion](../configuration.md#package-auto-deletion) | Cleanup policies |
| Database | SQLite, SQL Server, PostgreSQL, MySQL | Embedded database or PostgreSQL |
| Package storage | File system, Azure Blob, AWS S3, Google Cloud Storage, Aliyun OSS, Tencent COS | File system, S3, Azure Blob and more |
| Footprint | A single small .NET process, SQLite works out of the box | A larger Java-based server that needs more memory |

## Which one to pick

- Pick **Nexus** if you need one repository manager for many package formats.
- Pick **PaGetto** if you only need NuGet and want something small and free to run, with per-feed permissions, Entra ID sign-in and mirrors built in.

:::note

Details about Nexus Repository can change between editions and releases. Check the [Sonatype documentation](https://help.sonatype.com/) for the current feature set and limits.

:::
