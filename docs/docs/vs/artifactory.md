# Artifactory

[JFrog Artifactory](https://jfrog.com/artifactory/) is a commercial, universal artifact repository. It hosts packages for many ecosystems (NuGet, npm, Maven, Docker, PyPI and more) and is available self-hosted or as a JFrog cloud service.

## PaGetto vs Artifactory

| | PaGetto | Artifactory |
|---|---|---|
| License | Open source (MIT), free | Commercial. NuGet support needs a paid edition or a JFrog cloud plan |
| Package formats | NuGet packages and symbols | NuGet plus most other package formats and Docker images |
| Hosting | Self-hosted: Docker, Kubernetes (Helm), IIS, any ASP.NET Core 10 host | Self-hosted or JFrog cloud |
| NuGet protocol | v3 (push and delete on the standard `api/v2/package` endpoints) | v2 and v3 |
| Symbol server | Built in (`.snupkg`) | Check the JFrog documentation for your edition |
| Separate package sources | [Feeds](../feeds.md), each with its own settings and permissions | Local, remote and virtual repositories |
| Proxy of nuget.org | Per-feed [mirrors](../feeds.md#mirror-read-through-cache), several upstreams per feed, v3 or v2 | Remote repositories, combined with virtual repositories |
| Authentication | Local accounts, Microsoft Entra ID, groups, personal access tokens | Local users, LDAP, SAML, OpenID Connect, access tokens (depending on edition) |
| Retention | Per-feed version-based [auto-deletion](../configuration.md#package-auto-deletion) | Cleanup policies and scripting (depending on edition) |
| Database | SQLite, SQL Server, PostgreSQL, MySQL | Embedded or external database (see JFrog documentation) |
| Package storage | File system, Azure Blob, AWS S3, Google Cloud Storage, Aliyun OSS, Tencent COS | File system and major cloud object stores |
| Footprint | A single small .NET process, SQLite works out of the box | A larger Java-based platform with more moving parts |

## Which one to pick

- Pick **Artifactory** if you need one repository manager for many package formats, or its enterprise features (replication, advanced security scanning, a vendor support contract).
- Pick **PaGetto** if you only need NuGet, want something free and small to run, and want per-feed permissions and mirrors without a commercial license.

:::note

Details about Artifactory can change between editions and releases. Check the [JFrog documentation](https://jfrog.com/help/) for the current feature set.

:::
