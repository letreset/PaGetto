# CocoaPods pods

PaGetto can host pods for Swift and Objective-C next to NuGet, Dart, npm, Maven, Python, Rust, Go, Ruby, Hex and PHP packages. A feed of the type **CocoaPods** is a CocoaPods spec repository in the [CDN format](https://github.com/CocoaPods/Core/blob/master/lib/cocoapods-core/cdn_source.rb) that cdn.cocoapods.org uses: `pod install` reads pods from it, podspecs are published to it with a plain HTTP request, optionally with their source archive, and it can mirror [the public spec repository](https://cdn.cocoapods.org) or another CDN spec repository.

A feed serves one kind of package. Its type is chosen when the feed is created and can't be changed afterwards.

## Create a CocoaPods feed

On **Admin > Feeds**, press **New feed** and choose the type **CocoaPods** (see [Managing feeds](feeds.md#managing-feeds)). The spec repository is below `/cocoapods/`, clear of the pages of the web UI. CocoaPods is configured with the **Spec repository URL**:

```
https://your-server/feeds/ios/cocoapods/
```

The feed's **Connect** page shows it, with the settings below filled in.

## Configure CocoaPods

Name the feed as a `source` at the top of your `Podfile`:

```ruby
source 'https://your-server/feeds/ios/cocoapods/'
source 'https://cdn.cocoapods.org/'

target 'MyApp' do
  pod 'MyLib', '~> 1.0'
end
```

CocoaPods looks for each pod in the sources in order. Without a [mirror](#mirror-cdncocoapodsorg), name the public spec repository too, as above, so public pods still come from it. With a mirror of it, the feed's source is enough.

On the first `pod install`, CocoaPods reads `CocoaPods-version.yml` of the URL, sees that it is a CDN spec repository, and adds it to `~/.cocoapods/repos`. `pod repo add-cdn ios https://your-server/feeds/ios/cocoapods/` does the same with a name of your choice. Like any CDN source, CocoaPods only downloads the index files again on `pod repo update` or `pod install --repo-update`, so run one of them to see pods published after the last one.

### Authentication

CocoaPods sends the credentials of a server's host from `~/.netrc` with Basic authentication, both for the spec repository and for source archives (it downloads them with curl and `--netrc-optional`):

```
machine your-server
  login __token__
  password <token>
```

CocoaPods refuses a `.netrc` that other users can read, so run `chmod 600 ~/.netrc`. The machine is the host name, without a port. Without credentials, CocoaPods can't read `CocoaPods-version.yml`, takes the URL for a git repository and fails to clone it.

With `Authentication:Mode` set to `Local`, `Entra` or `Hybrid`, the password is a [personal access token](authentication.md#personal-access-tokens-pats), and the login is `__token__` or the token owner's username. The account needs the **pull** permission on the feed to install, and **push** to publish (see [Feed permissions](authentication.md#feed-permissions)).

With the `Legacy` mode, a feed without configured credentials needs no password to read. Publishing needs one of the configured API keys as the password; the login is not checked.

## Publish

CocoaPods has no command to publish to a CDN spec repository: `pod repo push` pushes to git spec repositories, and `pod trunk push` only to the public trunk. A podspec is published to the feed with `POST` as a multipart form, which needs the **push** permission. The podspec must be in its JSON form, which `pod ipc spec` writes from a Ruby podspec:

```bash
pod ipc spec MyLib.podspec > MyLib.podspec.json
curl --fail-with-body --netrc -F podspec=@MyLib.podspec.json \
  https://your-server/feeds/ios/cocoapods/api/v1/pods
```

The podspec's `source` says where CocoaPods gets the pod's files, usually a git repository and a tag, which every machine that installs the pod must be able to reach. To keep the source in the feed instead, publish its archive with the podspec:

```bash
git archive --format=zip -o MyLib-1.0.0.zip 1.0.0
curl --fail-with-body --netrc -F podspec=@MyLib.podspec.json -F source=@MyLib-1.0.0.zip \
  https://your-server/feeds/ios/cocoapods/api/v1/pods
```

The feed stores the archive and serves the podspec with a `source` that points at it: its `http` URL on the feed, its `type` and its `sha256`, which CocoaPods checks. A `flatten` of the published source is kept. CocoaPods unpacks a zip as it is, and a tar archive without its single top-level folder, so the podspec's paths such as `source_files` must match.

PaGetto checks each publish:

- The podspec must be JSON with a `name` and a `version`. A name is ASCII letters, digits and `_ + - . ! @ ( )`, not starting with a dot, at most 64 characters. Pod names are case-sensitive, as in CocoaPods: `MyLib` and `mylib` are two pods.
- The version must be one CocoaPods accepts, such as `1.0.0` or `2.0.0-beta.1`. A version with a letter or a hyphen is a prerelease.
- The podspec must have a `source`, unless its archive is published with it. The archive must be a `.zip`, `.tar.gz`, `.tgz`, `.tar.bz2`, `.tbz`, `.tar.xz`, `.txz` or `.tar` file; CocoaPods can only unpack a `.dmg` on macOS.
- The archive must not be larger than the feed's **Max package size**.
- A version that the feed already has is rejected with `409 Conflict`, unless the feed's **Package overwrite policy** allows replacing it.
- A read-only feed refuses every publish with `403 Forbidden`.

The feed answers `201 Created`, or an error with the reason as plain text, which `curl --fail-with-body` prints.

## The spec repository

The feed answers the requests CocoaPods makes of a CDN spec repository:

- `/cocoapods/CocoaPods-version.yml`: the CocoaPods versions that can read the repository, and the lengths of the hash prefixes that shard it.
- `/cocoapods/all_pods_versions_{a}_{b}_{c}.txt`: the pods of a shard, each with its versions. A pod's shard is the first three characters of the MD5 hash of its name.
- `/cocoapods/Specs/{a}/{b}/{c}/{name}/{version}/{name}.podspec.json`: the podspec of a version.
- `/cocoapods/all_pods.txt`: the names of the pods stored in the feed, for `pod search`. Pods that only the mirror has are not listed.
- `/cocoapods/deprecated_podspecs.txt`: empty. A pod is deprecated in its podspec.
- `/cocoapods/sources/{name}/{version}/{name}-{version}.{type}`: the source archive of a version that was published with one.

The files have ETags, so `pod repo update` only downloads what changed. An unknown pod is `404 Not Found`. An unlisted version is left out of the index, but its podspec can still be downloaded.

## Mirror cdn.cocoapods.org

A CocoaPods feed can mirror other spec repositories in the CDN format with the shards of cdn.cocoapods.org. On the feed's settings page, add a mirror with the URL `https://cdn.cocoapods.org` (see [Mirror](feeds.md#mirror-read-through-cache) for the settings every mirror has). Then:

- The index of a shard combines the versions published to the feed with those of the mirror. A version of the feed hides the mirror's version of the same text, and a version unlisted in the feed is left out even when the mirror has it.
- A podspec is downloaded from the mirror when CocoaPods first asks for it, stored in the feed as the mirror serves it, and served from there afterwards. CocoaPods downloads the podspecs of every version of a pod it resolves, so they are all stored.
- The mirror only has the podspecs: the pods' sources stay where their podspecs point, usually GitHub, which the machines that install the pods must be able to reach.
- With several mirrors, they are asked in order: the first one whose index lists a pod lists all of its versions. A mirror that is unreachable is skipped.
- The index files of mirrors are cached in memory like the version lists of NuGet mirrors (see [Upstream listing cache](feeds.md#upstream-listing-cache)).

The few public pods whose names have other characters, such as non-ASCII letters, are left out of the index and can't be mirrored.

## In the web UI

A CocoaPods feed lists the pods stored in it, with search. A pod's page shows the platforms and Swift versions it supports, the pods it depends on, its license and links, the versions, a download link for a source archive stored in the feed, and the `Podfile` lines. Pods that are only available from a mirror appear once a podspec has been downloaded.

Users with the **delete** permission can **unlist** a version, which leaves it out of the index while its podspec can still be downloaded, **relist** it, or delete it for good.

## What is different from NuGet feeds

| | CocoaPods feeds |
|---|---|
| Retention | The feed's own version limits, applied within an hour, and the removal of unused mirrored versions. See [auto-deletion](configuration.md#package-auto-deletion) |
| Deletion behavior | Not available |
| Upload from the browser | Not available. Publish with `curl` or another HTTP client |
| `pod trunk` and `pod repo push` | Not supported. Publish with the endpoint above |
| Ruby podspecs | Not accepted. Convert them with `pod ipc spec` |
| Full-text search of `pod search --full` | Not available |

The NuGet, pub, npm, Maven, PyPI, Cargo, Go, RubyGems, Hex and Composer APIs answer `404 Not Found` on a CocoaPods feed, and the spec repository answers `404 Not Found` on other feeds.
