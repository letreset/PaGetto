---
sidebar_position: 1
---

# Artifactory

[JFrog Artifactory](https://jfrog.com/artifactory/) is a commercial, universal artifact repository. It hosts packages and files for a long list of ecosystems, including container images, and is available self-hosted or as a JFrog cloud service. PaGetto is a smaller, open-source package server for 15 package formats, one per [feed](../feeds.md).

## PaGetto vs Artifactory

| | PaGetto | Artifactory |
|---|---|---|
| License and cost | Open source (MIT), free | Commercial editions and JFrog cloud plans. NuGet and most other formats need a paid edition or a cloud plan; the open-source edition is limited to Java build tools and generic repositories |
| Hosting | Self-hosted: Docker, Kubernetes (Helm), IIS, any ASP.NET Core 10 host | Self-hosted or JFrog cloud |
| Package formats | 15 package formats, NuGet with a symbol server (see [below](#package-formats)) | Almost every package format, plus Docker and OCI images, Helm charts, generic files and Linux packages |
| Separate package sources | [Feeds](../feeds.md) of one format each, with their own settings, mirrors and permissions. A feed serves its own and its mirrors' packages from one URL; there are no virtual feeds that combine several feeds | Local, remote and virtual repositories |
| Proxy of public registries | Per-feed [mirrors](../feeds.md#mirror-read-through-cache) for every format (nuget.org, npmjs, Maven Central, PyPI, crates.io, proxy.golang.org, rubygems.org, hex.pm, Packagist, CRAN, ...), several per feed in order, with optional upstream authentication | Remote repositories, combined with virtual repositories |
| Authentication | Local accounts, Microsoft Entra ID, groups, personal access tokens that can be limited to read-only or to one feed. No LDAP or SAML | Local users, LDAP, SAML, OpenID Connect, access tokens (depending on edition) |
| Permissions | Pull, push and delete per feed, granted to groups | Permission targets on repositories and paths |
| Retention | Per-feed version-based [auto-deletion](../configuration.md#package-auto-deletion) for every format, and removal of [unused mirrored versions](../configuration.md#unused-mirrored-packages) | Cleanup policies and scripting (depending on edition) |
| Integrations | Per-feed [webhooks](../feeds.md#webhook), an [audit log](../configuration.md#audit-log), OpenTelemetry [metrics](../configuration.md#metrics) | Webhooks, a REST API, the JFrog CLI and the rest of the JFrog platform (such as Xray security scanning) |
| Database | SQLite, SQL Server, PostgreSQL, MySQL | Embedded or external database (see JFrog documentation) |
| Package storage | File system, Azure Blob, AWS S3, Google Cloud Storage, Aliyun OSS, Tencent COS | File system and major cloud object stores |
| Footprint | A single small .NET process, SQLite works out of the box. Several replicas can share a database and cloud storage | A larger Java-based platform with more moving parts |

## Package formats

| Format | PaGetto | Artifactory |
|---|---|---|
| [NuGet](../nuget.md) | Yes, with symbols (`.snupkg`) | Yes |
| [Dart and Flutter (pub)](../dart.md) | Yes | Yes |
| [npm](../npm.md) | Yes | Yes |
| [Maven](../maven.md) | Yes | Yes |
| [Python (PyPI)](../pypi.md) | Yes | Yes |
| [Rust (Cargo)](../cargo.md) | Yes | Yes |
| [Go modules](../go.md) | Yes | Yes |
| [Ruby gems](../rubygems.md) | Yes | Yes |
| [Hex](../hex.md) | Yes | Yes |
| [PHP (Composer)](../composer.md) | Yes | Yes |
| [CocoaPods](../cocoapods.md) | Yes | Yes |
| [Swift](../swift.md) | Yes | Yes |
| [R (CRAN)](../cran.md) | Yes | Yes |
| [Ivy (sbt)](../ivy.md) | Yes | Yes |
| [Visual Studio extensions (VSIX)](../vsix.md) | Yes | Not among its package types (VS Code extensions are) |
| Docker and OCI images, Helm charts | No | Yes |
| Generic files, Debian, RPM, Alpine | No | Yes |
| Conda, Conan, Terraform, Hugging Face and others | No | Yes |

## Which one to pick

- Pick **Artifactory** if you need container images, generic file repositories or Linux packages next to your language packages, virtual repositories that combine several sources, or its enterprise features (replication, security scanning, a vendor support contract).
- Pick **PaGetto** if your packages are in the formats it hosts, and you want something free and small to run, with per-feed permissions, mirrors and retention without a commercial license.

:::note

Details about Artifactory can change between editions and releases. Check the [JFrog documentation](https://jfrog.com/help/) for the current feature set.

:::
