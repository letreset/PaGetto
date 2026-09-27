# Import nuget.org packages

## Mirroring

PaGetto doesn't copy all of nuget.org up front. Instead, a feed can mirror it: when a client asks the feed for a package it doesn't have, PaGetto fetches it from nuget.org, stores it in the feed and serves it. This is also known as "read-through caching".

For example, say you enable a nuget.org mirror on a feed and restore a project that uses [`Newtonsoft.Json`](https://www.nuget.org/packages/Newtonsoft.Json/). The feed doesn't have this package yet, so PaGetto downloads it from nuget.org. Later restores are served locally, and keep working when nuget.org is unreachable.

To set it up, add a mirror with the source `https://api.nuget.org/v3/index.json` on the feed's settings page. See [Mirror (read-through cache)](../feeds.md#mirror-read-through-cache).

To pre-fill a feed with the packages your projects use, restore those projects once against the feed. Every package they need, including transitive dependencies, is then stored in the feed.

## Importing package downloads from nuget.org

Packages mirrored from nuget.org start with a download count of zero. You can copy their download counts from nuget.org, so the web UI and search show how popular each package is.

The `import downloads` command downloads nuget.org's download statistics (a large JSON file) and updates the download count of every package version in the database that also exists on nuget.org. It updates all feeds, and versions that don't exist on nuget.org keep their count.

Run the command with the same configuration as the server, so it uses the same database. From the folder of the [release zip](../Installation/local.md):

```shell
dotnet PaGetto.dll import downloads
```

From a clone of the repository:

```shell
dotnet run --project src/PaGetto -- import downloads
```

In Docker, run it in the running container:

```shell
docker exec pagetto dotnet PaGetto.dll import downloads
```

The command exits when the import is done. It overwrites the counts of the matched versions, so downloads counted by PaGetto for those versions are replaced by nuget.org's numbers.
