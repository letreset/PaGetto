# Swift packages

A feed of the type **Swift** is a Swift package registry: it speaks the [registry API](https://github.com/swiftlang/swift-package-manager/blob/main/Documentation/PackageRegistry/Registry.md) of Swift Package Manager (SE-0292, with publishing from SE-0391), so `swift package resolve`, `swift build` and Xcode install packages from it, `swift package-registry publish` publishes to it, and it can mirror another Swift package registry.

A feed serves one kind of package. Its type is chosen when the feed is created and can't be changed afterwards.

## Create a Swift feed

On **Admin > Feeds**, press **New feed** and choose the type **Swift** (see [Managing feeds](feeds.md#managing-feeds)). The registry is below `/swift`, clear of the pages of the web UI. Swift Package Manager is configured with the **Registry URL**:

```
https://your-server/feeds/swift-packages/swift
```

The feed's **Connect** page shows it, with the commands below filled in.

Swift Package Manager only uses registries over HTTPS. `swift package-registry set` and `swift package-registry publish` accept `--allow-insecure-http` for local tests, but `swift package-registry login` has no such option, so a feed that needs credentials has to be served over HTTPS (see [Trusted reverse proxies](configuration.md#trusted-reverse-proxies) for running PaGetto behind one).

## Configure Swift Package Manager

Set the feed as the registry, for every package of your account or only for the package in the current folder:

```bash
swift package-registry set --global https://your-server/feeds/swift-packages/swift
```

Then depend on packages by their identifier, `scope.name`, instead of a git URL:

```swift
dependencies: [
    .package(id: "mona.LinkedList", from: "1.0.0"),
],
targets: [
    .target(name: "MyApp", dependencies: [
        .product(name: "LinkedList", package: "mona.LinkedList"),
    ]),
]
```

`swift package resolve` and `swift build` resolve the versions from the feed, download the source archives, check them against their checksums, and pin them in `Package.resolved`. Xcode uses the same registry configuration.

A registry can also serve a single scope, with `swift package-registry set --scope mona ...`, so other scopes come from another registry.

### Authentication

Sign in once with a personal access token:

```bash
swift package-registry login https://your-server/feeds/swift-packages/swift/login --token <token>
```

Swift Package Manager checks the token with the feed's `/login` endpoint, stores it in the keychain on macOS or in `~/.netrc` on Linux, and sends it as a Bearer token with every request to the server's host. `login` also takes the registry URL itself, and `--username` and `--password` for Basic authentication. The credentials belong to the host, so one token serves every feed of the server.

With `Authentication:Mode` set to `Local`, `Entra` or `Hybrid`, the token is a [personal access token](authentication.md#personal-access-tokens-pats). The account needs the **pull** permission on the feed to install, and **push** to publish (see [Feed permissions](authentication.md#feed-permissions)).

With the `Legacy` mode, a feed without configured credentials needs no token to read. Publishing needs one of the configured API keys as the token.

A request without credentials answers `401 Unauthorized` with the command to sign in as its message. The answer has no `WWW-Authenticate` header: Swift Package Manager sends its credentials without waiting for one, and on Linux it hangs on a 401 that has one.

## Publish

`swift package-registry publish` publishes the package in the current folder, which needs the **push** permission:

```bash
swift package-registry publish mona.LinkedList 1.0.0 --url https://your-server/feeds/swift-packages/swift
```

It writes the package into a source archive, a zip with the package in one folder, and sends it with the package's metadata. The metadata comes from `package-metadata.json` in the package's folder, or the file given with `--metadata-path`:

```json
{
  "description": "A linked list",
  "licenseURL": "https://github.com/mona/LinkedList/blob/main/LICENSE",
  "readmeURL": "https://github.com/mona/LinkedList/blob/main/README.md",
  "repositoryURLs": ["https://github.com/mona/LinkedList", "git@github.com:mona/LinkedList.git"],
  "author": {"name": "Mona"}
}
```

With `--signing-identity`, or `--private-key-path` and `--cert-chain-paths`, it signs the source archive and the manifests. The feed stores the signature of the archive and serves it with the release; it doesn't check it. Swift Package Manager checks the signatures it downloads against the trusted root certificates and the signing settings of its configuration (`swift package-registry` and `registries.json`), and remembers the signer of each package.

PaGetto checks each publish:

- The identifier is `scope.name`. A scope is letters, digits and single hyphens between them, at most 39 characters; a name is letters, digits and single hyphens or underscores between them, at most 100 characters. Identifiers are compared regardless of case: `mona.LinkedList` and `Mona.linkedlist` are the same package, which keeps the case it was first published with.
- The version must be a semantic version, such as `1.0.0` or `2.0.0-beta.1`. Versions that differ only in their build metadata (`1.0.0+abc`) are the same version.
- The source archive must be a zip with one folder at its top level and a `Package.swift` in it, as `swift package-registry publish` writes it: Swift Package Manager removes that folder when it unpacks the archive. Version-specific manifests such as `Package@swift-5.9.swift` next to it are kept too.
- The metadata, if any, must be a JSON object whose fields have the types Swift Package Manager reads.
- A signature must be in the format `cms-1.0.0`, the one Swift Package Manager signs with.
- The archive must not be larger than the feed's **Max package size**.
- A version that the feed already has is rejected with `409 Conflict`, unless the feed's **Package overwrite policy** allows replacing it.
- A read-only feed refuses every publish with `403 Forbidden`.

The feed answers `201 Created`, or an error as [problem details](https://www.rfc-editor.org/rfc/rfc7807) whose `detail` Swift Package Manager prints.

## The registry API

The feed answers the requests of Swift Package Manager, each with `Content-Version: 1`:

- `GET /swift/{scope}/{name}`: the releases of a package, newest first.
- `GET /swift/{scope}/{name}/{version}`: a release's metadata, with the SHA-256 checksum and the signature of its source archive.
- `GET /swift/{scope}/{name}/{version}/Package.swift`: the manifest, with the version-specific manifests in its `Link` header. With `?swift-version=`, the manifest of that Swift version, or a `303 See Other` to `Package.swift` when there is none.
- `GET /swift/{scope}/{name}/{version}.zip`: the source archive.
- `GET /swift/identifiers?url=`: the identifiers of the packages whose metadata names that repository URL, which `--replace-scm-with-registry` uses to swap git dependencies for registry packages. `https://github.com/mona/LinkedList.git`, `git@github.com:mona/LinkedList.git` and `https://github.com/mona/LinkedList` are the same repository.
- `POST /swift/login`: answers `200 OK` when the credentials are valid.
- `PUT /swift/{scope}/{name}/{version}`: publishes.

An unknown package or version is `404 Not Found`. A request for another version of the API is `415 Unsupported Media Type`. An unlisted release is left out of the release list, but can still be downloaded, so `Package.resolved` files that pin it keep working.

## Mirror another registry

A Swift feed can mirror other Swift package registries, such as another PaGetto feed. On the feed's settings page, add a mirror with the registry URL, such as `https://other-server/feeds/swift-packages/swift` (see [Mirror](feeds.md#mirror-read-through-cache) for the settings every mirror has, including its credentials). Then:

- The release list combines the releases published to the feed with those of the mirror. A release of the feed hides the mirror's release of the same version, and a release unlisted in the feed is left out even when the mirror has it.
- A release is downloaded from the mirror when Swift Package Manager first asks for it, checked against the mirror's checksum, stored in the feed with the mirror's metadata and signature, and served from there afterwards.
- `GET /swift/identifiers` also asks the mirrors.
- With several mirrors, they are asked in order: the first one that has a package lists all of its releases. A mirror that is unreachable is skipped.
- The release lists of mirrors are cached in memory, as on every feed type (see [Upstream listing cache](feeds.md#upstream-listing-cache)).

## In the web UI

A Swift feed lists the packages stored in it, with search. A package's page shows its identifier, the tools version of its `Package.swift`, its readme, author, license and repository links, whether it is signed, the versions with a download link for each source archive, and the `Package.swift` lines. Packages that are only available from a mirror appear once a release has been downloaded.

Users with the **delete** permission can **unlist** a version, which leaves it out of the release list while it can still be downloaded, **relist** it, or delete it for good.

## Feed settings and limits

Most [feed settings](feeds.md#feed-settings) apply to Swift feeds as to any other feed. These differ:

| | Swift feeds |
|---|---|
| Retention | The feed's own version limits, applied within an hour, and the removal of unused mirrored versions. See [auto-deletion](configuration.md#package-auto-deletion) |
| Deletion behavior | Not available |
| Upload from the browser | Not available. Publish with `swift package-registry publish` |
| Signatures | Stored and served, not checked by the server |
| Asynchronous publishing (`202 Accepted`) | Not used. Every publish is done when it answers |
| Search with `swift package-registry` | Not available. Search the web UI |

The APIs of other feed types answer `404 Not Found` on a Swift feed, and the registry API answers `404 Not Found` on feeds of other types.
