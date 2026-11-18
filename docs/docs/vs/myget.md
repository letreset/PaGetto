---
sidebar_position: 3
---

# MyGet

[MyGet](https://www.myget.org/) is a commercial, hosted package management service. It offers NuGet, npm, Bower, Maven, PHP Composer, Python, RubyGems and VSIX feeds as SaaS, with upstream sources and build services. PaGetto is a self-hosted, open-source package server for 15 package formats, one per [feed](../feeds.md).

## PaGetto vs MyGet

| | PaGetto | MyGet |
|---|---|---|
| License and cost | Open source (MIT), free. You pay only for the infrastructure you run it on | Subscription plans, with a limited free tier |
| Hosting | Self-hosted: Docker, Kubernetes (Helm), IIS, any ASP.NET Core 10 host | Hosted by MyGet (Enterprise plans on a dedicated URL) |
| Package formats | 15 package formats, NuGet with a symbol server (see [below](#package-formats)) | NuGet with symbols, npm, Bower, Maven, PHP Composer, Python, RubyGems, VSIX |
| Separate package sources | [Feeds](../feeds.md) of one format each, with their own settings, mirrors and permissions | Feeds per account, each with an endpoint per format |
| Proxy of public registries | Per-feed [mirrors](../feeds.md#mirror-read-through-cache) for every format (nuget.org, npmjs, Maven Central, PyPI, crates.io, ...), several per feed in order, with optional upstream authentication. Mirrored versions are stored in the feed | Upstream sources (nuget.org, npmjs, Maven Central, Bower and other MyGet feeds by default, other sources with Basic authentication), proxied or mirrored |
| Authentication | Local accounts, Microsoft Entra ID, groups, personal access tokens that can be limited to read-only or to one feed | MyGet accounts with external sign-in (Google, GitHub, Microsoft, and Entra ID or ADFS on Enterprise), API keys, pre-authenticated feed URLs |
| Retention | Per-feed version-based [auto-deletion](../configuration.md#package-auto-deletion) for every format, and removal of [unused mirrored versions](../configuration.md#unused-mirrored-packages) | Per feed: a maximum number of stable and prerelease versions, with pinned and depended-on packages kept |
| Integrations | Per-feed [webhooks](../feeds.md#webhook), an [audit log](../configuration.md#audit-log), OpenTelemetry [metrics](../configuration.md#metrics) | Webhooks for package and build events, build services |
| Where your data lives | Your database and your storage account or disk | MyGet's cloud |
| Internet access needed | No. It can run fully offline, with mirrors used when reachable | Yes |
| Quotas | Only the limits you configure (package size, retention) | Storage and feed limits depend on the plan |

## Package formats

| Format | PaGetto | MyGet |
|---|---|---|
| [NuGet](../nuget.md) | Yes, with symbols (`.snupkg`) | Yes, with symbols |
| [npm](../npm.md) | Yes | Yes |
| [Maven](../maven.md) | Yes | Yes |
| [Python (PyPI)](../pypi.md) | Yes | Yes |
| [Ruby gems](../rubygems.md) | Yes | Yes |
| [PHP (Composer)](../composer.md) | Yes | Yes |
| [Visual Studio extensions (VSIX)](../vsix.md) | Yes | Yes |
| [Dart and Flutter (pub)](../dart.md), [Rust (Cargo)](../cargo.md), [Go modules](../go.md), [Hex](../hex.md), [CocoaPods](../cocoapods.md), [Swift](../swift.md), [R (CRAN)](../cran.md), [Ivy (sbt)](../ivy.md) | Yes | No |
| Bower | No | Yes |

## Which one to pick

- Pick **MyGet** if its formats cover what you need, you want a hosted service with build services, and you don't want to run a server.
- Pick **PaGetto** if you want to keep packages in your own infrastructure, need one of the formats MyGet lacks (such as Rust, Go or Dart), avoid subscription costs, or run in a network without internet access.

:::note

Details about MyGet can change. Check the [MyGet documentation](https://docs.myget.org/) for current features and pricing.

:::
