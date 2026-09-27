# Local Feeds

[Local feeds](https://learn.microsoft.com/en-us/nuget/hosting-packages/local-feeds), also known as "folder feeds", let you use a folder as a NuGet package source. You can share them with a team through a network share.

## PaGetto vs local feeds

| | PaGetto | Local feed |
|---|---|---|
| Setup | Run a server (Docker, Kubernetes, IIS or a plain process) | Create a folder, optionally share it |
| Protocol | NuGet v3 over HTTP(S) | File system access (local path or UNC share) |
| Search | Yes, from the NuGet client and the web UI | No real search: the client has to read the folder |
| Speed with many packages | Fast, metadata is served from a database | Slows down as the folder grows, especially over a network share |
| Web UI | Browse packages, versions, dependencies and readmes | No |
| Publishing | `dotnet nuget push` with an API key or a personal access token | Copy files, or `nuget add` |
| Access control | Per-feed pull, push and delete permissions for users and groups | File share permissions only |
| Several sources | [Feeds](../feeds.md) with their own settings | One folder per source |
| Proxy of nuget.org | Per-feed [mirrors](../feeds.md#mirror-read-through-cache) | No |
| Retention and deletion | Per-feed [auto-deletion](../configuration.md#package-auto-deletion), unlist or hard delete | Delete files by hand |
| Symbol server | Built in (`.snupkg`) | No |
| Storage | File system or cloud storage (Azure, AWS, Google Cloud, Aliyun, Tencent) | Local disk or network share |
| Works outside the office network | Yes, over HTTPS | Only with VPN or file sharing across networks |

## Which one to pick

- Pick a **local feed** for a quick, single-developer setup, or to test a package before publishing it.
- Pick **PaGetto** once a team shares the packages, the feed grows, or you need search, a UI, access control or a nuget.org mirror.

To move an existing local feed, follow [Import packages from a local feed](../Import/local-feeds.md).
