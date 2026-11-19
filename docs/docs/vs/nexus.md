---
sidebar_position: 4
---

# Nexus

[Sonatype Nexus Repository](https://www.sonatype.com/products/sonatype-nexus-repository) is a universal repository manager. It hosts packages for many ecosystems, including container images, and comes as a free Community Edition with usage limits and a commercial Pro edition. PaGetto is a smaller, open-source package server for 16 package formats, one per [feed](../feeds.md).

## PaGetto vs Nexus

| | PaGetto | Nexus Repository |
|---|---|---|
| License and cost | Open source (MIT), free, no usage limits | Free Community Edition with limits on components and requests per day, commercial Pro edition |
| Hosting | Self-hosted: Docker, Kubernetes (Helm), IIS, any ASP.NET Core 10 host | Self-hosted; Pro also as a Sonatype cloud service |
| Package formats | 16 package formats, NuGet with a symbol server (see [below](#package-formats)) | Most language package formats, plus Docker and OCI images, Helm charts, raw files and Linux packages |
| Separate package sources | [Feeds](../feeds.md) of one format each, with their own settings, mirrors and permissions. A feed serves its own and its mirrors' packages from one URL; there are no group feeds that combine several feeds | Hosted, proxy and group repositories |
| Proxy of public registries | Per-feed [mirrors](../feeds.md#mirror-read-through-cache) for every format (nuget.org, npmjs, Maven Central, PyPI, crates.io, proxy.golang.org, rubygems.org, hex.pm, Packagist, CRAN, ...), several per feed in order, with optional upstream authentication | Proxy repositories, one upstream each, combined with group repositories |
| Authentication | Local accounts, Microsoft Entra ID, groups, personal access tokens that can be limited to read-only or to one feed. No LDAP or SAML | Local users, LDAP, API keys; SAML and OpenID Connect in Pro |
| Permissions | Pull, push and delete per feed, granted to groups | Roles and privileges per repository and format |
| Overwriting a version | Per feed: never, prerelease only, or always | Per hosted repository: allow redeploy, disable redeploy, or read-only |
| Retention | Per-feed version-based [auto-deletion](../configuration.md#package-auto-deletion) for every format, and removal of [unused mirrored versions](../configuration.md#unused-mirrored-packages) | Cleanup policies |
| Integrations | Per-feed [webhooks](../feeds.md#webhook), an [audit log](../configuration.md#audit-log), OpenTelemetry [metrics](../configuration.md#metrics) | Global and repository webhooks, a REST API |
| Database | SQLite, SQL Server, PostgreSQL, MySQL | Embedded H2 or PostgreSQL |
| Package storage | File system, Azure Blob, AWS S3, Google Cloud Storage, Aliyun OSS, Tencent COS | File system; S3, Azure Blob and Google Cloud Storage in Pro |
| High availability | Several replicas can share a database and cloud storage | Pro only |
| Footprint | A single small .NET process, SQLite works out of the box | A larger Java-based server that needs more memory |

## Package formats

| Format | PaGetto | Nexus Repository |
|---|---|---|
| [NuGet](../nuget.md) | Yes, with symbols (`.snupkg`) | Yes |
| [Dart and Flutter (pub)](../dart.md) | Yes | Yes |
| [npm](../npm.md) | Yes | Yes |
| [Maven](../maven.md) | Yes | Yes |
| [Python (PyPI)](../pypi.md) | Yes | Yes |
| [Rust (Cargo)](../cargo.md) | Yes | Yes |
| [Go modules](../go.md) | Yes | Yes |
| [Ruby gems](../rubygems.md) | Yes | Yes |
| [Hex](../hex.md) | Yes | No |
| [PHP (Composer)](../composer.md) | Yes | Yes |
| [CocoaPods](../cocoapods.md) | Yes | Proxy only, no hosted pods |
| [Swift](../swift.md) | Yes | Yes |
| [R (CRAN)](../cran.md) | Yes | Yes |
| [Ivy (sbt)](../ivy.md) | Yes | No Ivy format (sbt can use Maven repositories) |
| [Visual Studio extensions (VSIX)](../vsix.md) | Yes | No |
| [VS Code extensions](../vscode.md) | Yes | No |
| Docker and OCI images, Helm charts | No | Yes |
| Raw files, APT, Yum, Alpine | No | Yes |
| Conda, Conan, Terraform, Ansible, Git LFS, Hugging Face (proxy) and others | No | Yes |

## Which one to pick

- Pick **Nexus** if you need container images, raw file repositories or Linux packages next to your language packages, or group repositories that combine several sources.
- Pick **PaGetto** if your packages are in the formats it hosts (including Hex, Ivy and Visual Studio extensions, which Nexus lacks), and you want something small and free to run without usage limits, with per-feed permissions, Entra ID sign-in, mirrors and retention built in.

:::note

Details about Nexus Repository can change between editions and releases. Check the [Sonatype documentation](https://help.sonatype.com/) for the current feature set and limits.

:::
