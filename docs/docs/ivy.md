# Ivy repositories for sbt

A feed of the type **Ivy** is an Ivy repository for sbt plugins and Scala and Java libraries, in the layout that sbt publishes with `Resolver.ivyStylePatterns`: sbt resolves from it and publishes to it, and it can mirror the [sbt plugin repository](https://repo.scala-sbt.org/scalasbt/sbt-plugin-releases) or another Ivy repository in the same layout.

Most libraries, and sbt plugins from sbt 1.9 on, are published in the Maven layout; use a [Maven feed](maven.md) for those. An Ivy feed is for builds that publish with `publishMavenStyle := false`, and for the older sbt plugins that are only in the Ivy layout.

A feed serves one kind of package. Its type is chosen when the feed is created and can't be changed afterwards.

## Create an Ivy feed

On **Admin > Feeds**, press **New feed** and choose the type **Ivy** (see [Managing feeds](feeds.md#managing-feeds)). The repository is below `/ivy`, clear of the pages of the web UI:

```
https://your-server/feeds/sbt/ivy
```

The feed's **Connect** page shows the repository URL with the settings below filled in.

## The layout

The paths are those of sbt's Ivy patterns:

```
{organisation}/{module}[/scala_{scalaVersion}][/sbt_{sbtVersion}]/{revision}/{type}s/{artifact}[-{classifier}].{ext}
```

for example `com.example/sbt-tool/scala_2.12/sbt_1.0/1.0.0/jars/sbt-tool.jar` for an sbt plugin and `com.example/my-lib_2.13/1.0.0/jars/my-lib_2.13.jar` for a library. The organisation keeps its dots. Each revision has a descriptor at `{revision}/ivys/ivy.xml`, and every file has its checksums next to it (`.sha1`, `.md5`, `.sha256`, `.sha512`).

The folder of a module, the path up to the revision with a trailing slash, is a directory listing of its revisions. sbt reads it to resolve a dynamic revision such as `0.+` or `latest.integration`.

A module is named `organisation:module` in the web UI, followed by `:scala_{version}:sbt_{version}` for an sbt plugin, since a plugin is published once per sbt version under the same revision. Branches (`[branch]` in the pattern) are not supported.

## Configure sbt

Add the feed as a resolver with the Ivy patterns, and the credentials from a file in your home folder:

```scala
// build.sbt
resolvers += Resolver.url("sbt", url("https://your-server/feeds/sbt/ivy"))(Resolver.ivyStylePatterns)
credentials += Credentials(Path.userHome / ".sbt" / ".pagetto-credentials")
```

```properties
# ~/.sbt/.pagetto-credentials
realm=PaGetto
host=your-server
user=USERNAME
password=YOUR_TOKEN
```

sbt plugins are resolved by the build of the build, so for them the same two lines go into `project/plugins.sbt`, next to the plugin:

```scala
// project/plugins.sbt
resolvers += Resolver.url("sbt", url("https://your-server/feeds/sbt/ivy"))(Resolver.ivyStylePatterns)
credentials += Credentials(Path.userHome / ".sbt" / ".pagetto-credentials")
addSbtPlugin("com.example" % "sbt-tool" % "1.0.0")
```

sbt refuses plain HTTP resolvers; for a test server without HTTPS, add `.withAllowInsecureProtocol(true)` to the resolver.

To send every download through the feed, for example when it mirrors the sbt plugin repository, list the repositories in `~/.sbt/repositories` and start sbt with `-Dsbt.override.build.repos=true`:

```
[repositories]
  local
  sbt-ivy: https://your-server/feeds/sbt/ivy/, [organisation]/[module]/(scala_[scalaVersion]/)(sbt_[sbtVersion]/)[revision]/[type]s/[artifact](-[classifier]).[ext]
  maven-central
```

### Authentication

sbt matches credentials by the realm and host of the server's challenge; the realm is `PaGetto`, as for [Maven feeds](maven.md#sbt). With `Authentication:Mode` set to `Local`, `Entra` or `Hybrid`, the password is a [personal access token](authentication.md#personal-access-tokens-pats) (a local account's own password works too). The account needs the **pull** permission on the feed to resolve, and **push** to publish (see [Feed permissions](authentication.md#feed-permissions)).

With the `Legacy` mode, a feed without configured credentials needs no password to resolve. Publishing needs one of the configured API keys as the password; the username is not checked.

## Publish

```scala
// build.sbt; run sbt publish
publishMavenStyle := false
publishTo := Some(Resolver.url("sbt", url("https://your-server/feeds/sbt/ivy"))(Resolver.ivyStylePatterns))
credentials += Credentials(Path.userHome / ".sbt" / ".pagetto-credentials")
```

`sbt publish` uploads the JAR, the sources and docs JARs and the `ivy.xml`, each with its SHA-1 and MD5. PaGetto checks each upload:

- The path must be a file of the layout above, with letters, digits, `.`, `-`, `_` and `+`. The module name with its cross versions is at most 128 characters, the revision at most 128 and the file below the revision's folder at most 256.
- An `ivy.xml` must be an Ivy file whose organisation, module, revision and the `e:scalaVersion` and `e:sbtVersion` of an sbt plugin are those of its path.
- A file must not be larger than the feed's **Max package size**, which applies to each file.
- A file that the revision already has is rejected with `409 Conflict`, unless the feed's **Package overwrite policy** allows replacing it. Uploading the same content again succeeds, since clients retry. The files of `SNAPSHOT` revisions can always be replaced, as sbt republishes them in place.
- A read-only feed refuses every upload with `403 Forbidden`.

Uploaded checksums are compared with the stored file and rejected when they differ. sbt checks whether a file exists before it uploads it, so publishing a revision again fails in sbt with `destination file exists and overwrite == false`; with `publishConfiguration := publishConfiguration.value.withOverwrite(true)` the server decides, by the overwrite policy.

## Mirror the sbt plugin repository

An Ivy feed can mirror other Ivy repositories in the same layout. On the feed's settings page, add a mirror with the URL `https://repo.scala-sbt.org/scalasbt/sbt-plugin-releases` (see [Mirror](feeds.md#mirror-read-through-cache) for the settings every mirror has). Then:

- The directory listing of a module has the revisions published to the feed and those the mirror lists in its own directory listing. Listings of mirrors are cached in memory, as on every feed type (see [Upstream listing cache](feeds.md#upstream-listing-cache)).
- A file of a revision is downloaded from the mirror on its first use, checked against the `.sha1` the mirror lists, stored in the feed and served from there afterwards.
- A revision published to the feed hides the same revision of the mirror: a file it doesn't have is not fetched from the mirror.
- `SNAPSHOT` revisions are not mirrored.
- With several mirrors, the first one whose listing has a module lists its revisions, and the first one that has a file serves it. A mirror that is unreachable is skipped.

## In the web UI

An Ivy feed lists the modules stored in it, with search. A module's page shows the description, homepage, license and status of its `ivy.xml`, the sbt and Scala versions of a plugin, the dependencies, the files of the revision with their SHA-1, the revisions, and the line for `build.sbt` (or `project/plugins.sbt` for a plugin). Modules that are only available from a mirror appear once a file has been downloaded.

Users with the **delete** permission can **unlist** a revision, which leaves it out of the directory listing (it can still be resolved by its exact revision), **relist** it, or delete it with its files for good.

## Feed settings and limits

Most [feed settings](feeds.md#feed-settings) apply to Ivy feeds as to any other feed. These differ:

| | Ivy feeds |
|---|---|
| Retention | The feed's own version limits, applied within an hour, and the removal of unused mirrored versions. See [auto-deletion](configuration.md#package-auto-deletion) |
| Deletion behavior | Not available: there is no delete in the repository layout |
| Upload from the browser | Not available. Publish with sbt |
| Branches | Not supported |
| Search | Only the web UI searches; the repository layout has no search |

The APIs of other feed types answer `404 Not Found` on an Ivy feed, and the Ivy repository answers `404 Not Found` on feeds of other types.

