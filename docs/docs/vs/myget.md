# MyGet

[MyGet](https://www.myget.org/) is a commercial, hosted package management service. It offers NuGet, npm, Maven, Python and other feeds as SaaS, with upstream sources and build services.

## PaGetto vs MyGet

| | PaGetto | MyGet |
|---|---|---|
| License and cost | Open source (MIT), free. You pay only for the infrastructure you run it on | Subscription plans, with a limited free tier |
| Hosting | Self-hosted: Docker, Kubernetes (Helm), IIS, any ASP.NET Core 10 host | Hosted by MyGet |
| Package formats | NuGet packages and symbols | NuGet, npm, Maven, Python, VSIX and more |
| Separate package sources | [Feeds](../feeds.md), each with its own settings and permissions | Feeds per account |
| Proxy of nuget.org | Per-feed [mirrors](../feeds.md#mirror-read-through-cache), several upstreams per feed | Upstream sources |
| Authentication | Local accounts, Microsoft Entra ID, groups, personal access tokens | MyGet accounts, API keys, feed-level access |
| Symbol server | Built in (`.snupkg`) | Yes |
| Where your data lives | Your database and your storage account or disk | MyGet's cloud |
| Internet access needed | No. It can run fully offline, with mirrors used when reachable | Yes |
| Quotas | Only the limits you configure (package size, retention) | Storage and feed limits depend on the plan |

## Which one to pick

- Pick **MyGet** if you want a hosted service for several package formats and don't want to run a server.
- Pick **PaGetto** if you want to keep packages in your own infrastructure, avoid subscription costs, or run in a network without internet access.

:::note

Details about MyGet can change. Check the [MyGet documentation](https://docs.myget.org/) for current features and pricing.

:::
