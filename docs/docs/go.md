# Go modules

PaGetto can host Go modules next to NuGet, Dart, npm, Maven, Python and Rust packages. A feed of the type **Go** is a [module proxy](https://go.dev/ref/mod#goproxy-protocol): the go command downloads modules from it, modules are uploaded to it with a plain HTTP request, and it can mirror [proxy.golang.org](https://proxy.golang.org) or another module proxy.

A feed serves one kind of package. Its type is chosen when the feed is created and can't be changed afterwards.

## Create a Go feed

On **Admin > Feeds**, press **New feed** and choose the type **Go** (see [Managing feeds](feeds.md#managing-feeds)). The proxy is below `/go`, clear of the pages of the web UI. The go command is configured with the **GOPROXY URL**:

```
https://your-server/feeds/golang/go
```

The feed's **Connect** page shows it, with the settings below filled in.

## Configure the go command

Set `GOPROXY` to the feed. Without a [mirror](#mirror-proxygolangorg), list the public proxy after it: the go command asks the next proxy of the list when one answers `404 Not Found`.

```bash
go env -w GOPROXY=https://your-server/feeds/golang/go,https://proxy.golang.org,direct
```

With a mirror of proxy.golang.org, the feed alone is enough:

```bash
go env -w GOPROXY=https://your-server/feeds/golang/go
```

The go command checks every module it downloads against the public [checksum database](https://go.dev/ref/mod#checksum-database), which doesn't know private modules. Leave them out of the check with `GONOSUMDB`, a comma-separated list of path patterns:

```bash
go env -w GONOSUMDB=example.com/*
```

Don't use `GOPRIVATE` for this: it also sets `GONOPROXY`, which makes the go command fetch those modules from version control instead of from the feed.

Then use the modules as usual: `go get example.com/my/module@v1.0.0`, or a `require` in `go.mod`.

### Authentication

The go command sends the credentials of a server's host from your [`.netrc` file](https://go.dev/ref/mod#private-module-proxy-auth) (`~/.netrc`, or `%USERPROFILE%\_netrc` on Windows) with Basic authentication:

```
machine your-server
login __token__
password <token>
```

The go command only sends credentials over HTTPS: it refuses to send them to an `http://` URL, so a server that needs credentials must be reached with HTTPS.

With `Authentication:Mode` set to `Local`, `Entra` or `Hybrid`, the password is a [personal access token](authentication.md#personal-access-tokens-pats), and the login is `__token__` or the token owner's username. The account needs the **pull** permission on the feed to download, and **push** to upload (see [Feed permissions](authentication.md#feed-permissions)).

With the `Legacy` mode, a feed without configured credentials needs no password to read. Uploading needs one of the configured API keys as the password; the username is not checked.

## Upload

Go has no command to publish a module: modules usually come from version control. A module zip is uploaded to the feed with `PUT` at the URL the go command downloads it from, which needs the **push** permission:

```bash
git archive --format=zip --prefix=example.com/my/module@v1.0.0/ -o module.zip v1.0.0
curl --fail-with-body -u __token__:<token> -T module.zip \
  https://your-server/feeds/golang/go/example.com/my/module/@v/v1.0.0.zip
```

The zip has the [layout the go command expects](https://go.dev/ref/mod#zip-files): every file is in a folder named `{module}@{version}/`, with the module's `go.mod` at its root. `git archive` writes it from a tag when the repository is the module. The folder entries it adds are left out by the feed, because `go mod verify` fails for a zip that has them. `git archive` also takes the files of nested modules and `vendor` folders, which the go command leaves out of a module, so a repository with those needs the [`golang.org/x/mod/zip`](https://pkg.go.dev/golang.org/x/mod/zip) package to build the zip. The `.info` and `.mod` files are made by the feed from the zip.

In the URL, each uppercase letter of the module path and version is written as `!` followed by the lowercase letter, as the go command does: `github.com/Contoso/tool` is uploaded to `.../go/github.com/!contoso/tool/@v/v1.0.0.zip`.

PaGetto checks each upload:

- The module path must be valid for the go command: path elements of ASCII letters, digits and `-._~`, a first element with a dot and without uppercase letters, and at most 128 characters once escaped.
- The version must be a canonical version such as `v1.2.3`, `v1.2.3-rc.1` or a pseudo-version. A path ending with `/vN` takes versions of major version N; any other path versions `v0` and `v1`. `+incompatible` versions are for modules without a `go.mod` file, so they can't be uploaded.
- The zip must have its files in the `{module}@{version}/` folder, a `go.mod` there that declares the module, no file twice regardless of case, and at most 500 MiB once decompressed.
- The zip must not be larger than the feed's **Max package size**.
- A version that the feed already has is rejected with `409 Conflict`, unless the feed's **Package overwrite policy** allows replacing it. Go modules are meant to never change: the go command records a checksum of each version in `go.sum` and stops when it gets other content.
- A read-only feed refuses every upload with `403 Forbidden`.

The feed answers `201 Created`, or an error with the reason as plain text, which `curl --fail-with-body` prints.

## The proxy

The feed answers the requests of the [GOPROXY protocol](https://go.dev/ref/mod#goproxy-protocol), with the module path and version escaped:

- `/go/{module}/@v/list`: the module's versions, one per line, without pseudo-versions. A module that only has pseudo-versions has an empty list.
- `/go/{module}/@latest`: the `.info` of the newest release, or of the newest prerelease when there is none, or of the newest pseudo-version when there is no tagged version.
- `/go/{module}/@v/{version}.info`: the version and its time, as JSON.
- `/go/{module}/@v/{version}.mod`: the version's `go.mod` file.
- `/go/{module}/@v/{version}.zip`: the module zip.

An unknown module or version is `404 Not Found`, which makes the go command try the next proxy of `GOPROXY`.

## Mirror proxy.golang.org

A Go feed can mirror other module proxies. On the feed's settings page, add a mirror with the URL `https://proxy.golang.org` (see [Mirror](feeds.md#mirror-read-through-cache) for the settings every mirror has). Then:

- A module's list combines the versions uploaded to the feed with those of the mirror, and `@latest` takes both into account.
- The `.info` and `.mod` files of a version that the feed doesn't have come from the mirror. The go command asks for them for every module in the build list, also those whose code it doesn't need.
- A version is downloaded from the mirror when its zip is first asked for, stored in the feed with the mirror's `.info` and `.mod`, and served from there afterwards.
- A version uploaded to the feed hides the same version of the mirror, and a version unlisted in the feed is left out of the list even when the mirror has it.
- A query that is not a version, such as `go get example.com/tool@main`, is passed on to the mirror.
- With several mirrors, they are asked in order: the first one that lists a module lists all of its versions, and the first one that has a version serves it. A mirror that is unreachable is skipped.
- The version lists of mirrors are cached in memory like the version lists of NuGet mirrors (see [Upstream listing cache](feeds.md#upstream-listing-cache)).

The checksums of mirrored modules are still checked against the checksum database, unless `GONOSUMDB` or `GOSUMDB=off` turns that off.

## In the web UI

A Go feed lists the modules stored in it, with search, under their module paths. A module's page shows the readme (`README.md` of the zip), the Go version and the direct requirements of its `go.mod`, the versions, a download link, and the snippets for `go get` and `go.mod`. Modules that are only available from a mirror appear once their zip has been downloaded.

Users with the **delete** permission can **unlist** a version, which leaves it out of the list and `@latest` while it can still be downloaded, **relist** it, or delete it for good.

## What is different from NuGet feeds

| | Go feeds |
|---|---|
| Retention | Not available. Old versions are only removed by hand |
| Deletion behavior | Not available |
| Upload from the browser | Not available. Upload with `curl` or another HTTP client |
| Retractions | Not checked by the feed: the go command reads the `retract` directives of the latest `go.mod` itself |
| Checksum database | Not served. Private modules need `GONOSUMDB` |
| Direct access to version control | Not available. Modules come from uploads and mirrors |

The NuGet, pub, npm, Maven, PyPI and Cargo APIs answer `404 Not Found` on a Go feed, and the module proxy answers `404 Not Found` on other feeds.
