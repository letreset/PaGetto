# Dart and Flutter packages

PaGetto can host Dart and Flutter packages next to NuGet packages. A feed of the type **Dart** is a [hosted pub repository](https://dart.dev/tools/pub/custom-package-repositories): `dart pub` and `flutter pub` publish to it and fetch from it, and it can mirror [pub.dev](https://pub.dev) or another pub server.

A feed serves one kind of package. Its type is chosen when the feed is created and can't be changed afterwards, so NuGet and Dart packages live in separate feeds with their own settings and permissions.

## Create a Dart feed

On **Admin > Feeds**, press **New feed** and choose the type **Dart** (see [Managing feeds](feeds.md#managing-feeds)). The feed's **hosted URL** is its address without anything after the slug:

```
https://your-server/feeds/dart
```

The feed's **Connect** page shows this URL with the commands below filled in.

:::info

The default feed is always a NuGet feed, so a Dart feed always has a `/feeds/{slug}` URL.

:::

## Sign in

With `Authentication:Mode` set to `Local`, `Entra` or `Hybrid`, the pub client authenticates with a [personal access token](authentication.md#personal-access-tokens-pats):

```shell
dart pub token add https://your-server/feeds/dart
```

Paste the token when the client asks for it. On a build agent, read it from an environment variable instead:

```shell
dart pub token add https://your-server/feeds/dart --env-var PAGETTO_TOKEN
```

The account needs the **pull** permission on the feed to fetch packages and the **push** permission to publish (see [Feed permissions](authentication.md#feed-permissions)).

:::warning

The pub client only sends tokens over HTTPS, or to `localhost`. Serve PaGetto over HTTPS before using a Dart feed with user accounts.

:::

With the `Legacy` mode, a feed without configured credentials needs no token to fetch packages. Publishing needs one of the configured API keys as the token. A server that has both credentials and API keys configured can't publish Dart packages; use one of the other modes there.

## Use a package

Name the feed as the package's source in `pubspec.yaml`:

```yaml
dependencies:
  my_package:
    hosted: https://your-server/feeds/dart
    version: ^1.0.0
```

Or add it from the command line:

```shell
dart pub add my_package:^1.0.0 --hosted-url https://your-server/feeds/dart
```

To fetch every package through the feed instead of pub.dev, set `PUB_HOSTED_URL`. This needs a feed that [mirrors pub.dev](#mirror-pubdev):

```shell
export PUB_HOSTED_URL=https://your-server/feeds/dart
```

:::tip

The pub client looks up a dependency that has no `hosted` URL on the default repository. So when a package in the feed depends on another package in the feed, either give that dependency its `hosted` URL in the package's `pubspec.yaml`, or have everyone who uses the package set `PUB_HOSTED_URL`.

:::

## Publish a package

Set the feed as the package's publishing target in its `pubspec.yaml`, then publish:

```yaml
publish_to: https://your-server/feeds/dart
```

```shell
dart pub publish
```

PaGetto checks the uploaded archive before it accepts it:

- It must be a gzipped tar archive with a `pubspec.yaml` at its root, of at most 1 GiB once decompressed.
- The `name` must be a lowercase Dart identifier (`a-z`, `0-9` and `_`) of at most 64 characters, and the `version` a semantic version of at most 128 characters.
- The archive must not be larger than the feed's **Max package size**.
- A version that already exists is rejected, unless the feed's **Package overwrite policy** allows replacing it.

When a package is rejected, `dart pub publish` shows the reason. Unlike NuGet, pub treats build metadata as part of the version, so `1.0.0+1` and `1.0.0+2` are two versions.

## Mirror pub.dev

A Dart feed can mirror other pub servers. On the feed's settings page, add a mirror with the URL `https://pub.dev` (see [Mirror](feeds.md#mirror-read-through-cache) for the settings every mirror has). Then:

- A package's version list combines the versions published to the feed with those of the mirror. A version published to the feed hides the same version of the mirror.
- A version is downloaded from the mirror on its first use, checked against the SHA-256 hash the mirror lists, stored in the feed and served from there afterwards.
- With several mirrors, they are asked in order and the first one that has the package serves all of its versions. A mirror that is unreachable is skipped.
- Version lists from mirrors are cached in memory like those of NuGet mirrors (see [Upstream listing cache](feeds.md#upstream-listing-cache)).

A mirror's credentials are only sent to the mirror itself, not to another host its archives are served from.

## In the web UI

A Dart feed lists the packages stored in it, with search. A package's page shows its readme, dependencies, SDK constraints and versions, and the lines to add to `pubspec.yaml`. Packages that are only available from a mirror appear once a version has been downloaded.

Users with the **delete** permission can **retract** a version (pub's word for unlisting: the version stays downloadable, but clients don't pick it for new installs), **restore** it, or delete it for good.

Raw HTML in a package's readme is shown as text.

## What is different from NuGet feeds

| | Dart feeds |
|---|---|
| Retention | Not available. Old versions are only removed by hand |
| Deletion behavior | The setting applies to the NuGet API only. In the web UI, retracting and deleting are separate buttons |
| Upload from the browser | Not available. Publish with `dart pub publish` |
| Symbols, Atom feed, search API | NuGet only |
| Security advisories | Not served |

The NuGet API answers `404 Not Found` on a Dart feed, and the pub API answers `404 Not Found` on a NuGet feed.
