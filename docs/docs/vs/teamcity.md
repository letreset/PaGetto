# TeamCity

[JetBrains TeamCity](https://www.jetbrains.com/teamcity/) is a CI/CD server. It includes a built-in [NuGet feed](https://www.jetbrains.com/help/teamcity/using-teamcity-as-nuget-feed.html) that serves the `.nupkg` files your builds publish as artifacts.

## PaGetto vs TeamCity's NuGet feed

| | PaGetto | TeamCity NuGet feed |
|---|---|---|
| Purpose | A dedicated NuGet and symbol server | A feature of a CI server |
| License | Open source (MIT), free | Part of TeamCity (free and commercial editions) |
| How packages get in | `dotnet nuget push` from any CI system or developer machine | Indexed from build artifacts of TeamCity builds |
| Package lifetime | Kept until you delete them or [retention](../configuration.md#package-auto-deletion) removes them | Tied to build artifacts, so build cleanup rules can remove packages |
| Separate package sources | [Feeds](../feeds.md), each with its own settings and permissions | One feed per TeamCity project |
| Proxy of nuget.org | Per-feed [mirrors](../feeds.md#mirror-read-through-cache) | No |
| Authentication | Local accounts, Microsoft Entra ID, groups, personal access tokens | TeamCity users and tokens, optional guest access |
| Symbol server | Built in (`.snupkg`) | Through a separate TeamCity plugin |
| Web UI | Package browser with readmes and dependencies | Packages appear as build artifacts |
| Requires | A small server of its own | A TeamCity installation |

## Which one to pick

- Use **TeamCity's feed** if all your packages are built in TeamCity and you only need to share them between TeamCity projects.
- Pick **PaGetto** if packages come from several CI systems, must outlive their builds, or you need a nuget.org mirror, symbol server or per-feed permissions. TeamCity can push to PaGetto with its NuGet publish step.
