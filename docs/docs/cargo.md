# Rust crates

PaGetto can host Rust crates next to NuGet, Dart, npm, Maven and Python packages. A feed of the type **Rust** is a Cargo registry: Cargo installs crates from it, `cargo publish` and `cargo yank` work against it, and it can mirror [crates.io](https://crates.io) or another sparse registry.

A feed serves one kind of package. Its type is chosen when the feed is created and can't be changed afterwards.

## Create a Rust feed

On **Admin > Feeds**, press **New feed** and choose the type **Rust** (see [Managing feeds](feeds.md#managing-feeds)). The registry is below `/cargo/`, clear of the pages of the web UI. Cargo is configured with the **index URL**, which uses the [sparse protocol](https://doc.rust-lang.org/cargo/reference/registry-index.html#sparse-protocol):

```
sparse+https://your-server/feeds/rust/cargo/index/
```

The feed's **Connect** page shows it, with the settings below filled in. They need Cargo 1.74 or later, the first version that sends a token to read from a registry.

## Configure Cargo

Add the feed as a registry in `.cargo/config.toml`, in the project or in `~/.cargo/config.toml`. The examples name the registry after the feed's slug, `rust`:

```toml
[registries.rust]
index = "sparse+https://your-server/feeds/rust/cargo/index/"
credential-provider = ["cargo:token"]
```

Cargo refuses to use a registry that asks for a token on every request unless a credential provider is configured; `cargo:token` reads the token from `cargo login` or the environment. `[registry] global-credential-providers = ["cargo:token"]` does the same for every registry.

Then give Cargo a token: run `cargo login --registry rust` and paste it, which stores it in `~/.cargo/credentials.toml`, or set the environment variable `CARGO_REGISTRIES_RUST_TOKEN`.

A dependency names the registry it comes from:

```toml
[dependencies]
my_crate = { version = "1.0", registry = "rust" }
```

or, with Cargo, `cargo add my_crate --registry rust`.

To get the crates of crates.io through a feed that [mirrors it](#mirror-cratesio), replace crates.io with the feed as well:

```toml
[source.crates-io]
replace-with = "rust"
```

### Authentication

With `Authentication:Mode` set to `Local`, `Entra` or `Hybrid`, the token is a [personal access token](authentication.md#personal-access-tokens-pats). Cargo sends it as the whole `Authorization` header, without a scheme; `Bearer` followed by the token works too. The feed's `config.json` says `"auth-required": true`, so Cargo sends the token with every request, also to read. The account needs the **pull** permission on the feed to install, **push** to publish, and **delete** to yank (see [Feed permissions](authentication.md#feed-permissions)).

With the `Legacy` mode, a feed without configured credentials needs no token to read, and its `config.json` says so. Publishing and yanking need one of the configured API keys as the token. Cargo can't send a username and password, so a `Legacy` server with configured credentials can't serve Cargo.

## Publish

Keep the crate from being published to crates.io by mistake, in its `Cargo.toml`:

```toml
[package]
publish = ["rust"]
```

then run `cargo publish --registry rust`. PaGetto checks each crate:

- The name must be ASCII letters, digits, `-` and `_`, start with a letter and have at most 64 characters, as on crates.io. Names are matched regardless of case, and a crate keeps the case of its first version: publishing `my_crate` to a feed that has `My_Crate` is refused.
- The version must be a [semantic version](https://semver.org). Like on crates.io, versions that differ only in their build metadata (`1.0.0+a` and `1.0.0+b`) are the same version.
- The `.crate` file must have the crate's files in a `{name}-{version}` folder with its `Cargo.toml`, as `cargo package` writes it.
- The crate must not be larger than the feed's **Max package size**.
- A version that the feed already has is rejected with `409 Conflict`, unless the feed's **Package overwrite policy** allows replacing it.
- A read-only feed refuses every publish and yank with `403 Forbidden`.

Cargo looks for the version in the index before it uploads, and stops with `crate my_crate@1.0.0 already exists` itself. For the other checks it shows the reason the feed gives, for example `the remote server responded with an error (status 400 Bad Request): The crate is named my_crate in this feed, not My_Crate.`

The readme Cargo sends with the crate (the `readme` of `Cargo.toml`, `README.md` by default) is shown on the crate's page.

### Yank

`cargo yank --registry rust my_crate@1.0.0` yanks a version, and `cargo yank --registry rust my_crate@1.0.0 --undo` takes it back. A yanked version stays in the index and can still be downloaded, so lock files that name it keep working, but Cargo doesn't pick it for new dependencies. Yanking needs the **delete** permission, as unlisting does on other feeds.

`cargo search --registry rust` searches the crates stored in the feed. Owners (`cargo owner`) are not supported: permissions come from the feed.

## The registry

The feed answers what Cargo asks a registry:

- `/cargo/index/config.json`: the URLs of the API and the downloads, and whether Cargo has to send a token.
- `/cargo/index/{prefix}/{name}`: a crate's index file, one JSON line per version with its dependencies, features, SHA-256 checksum and yanked state. The prefix depends on the length of the lowercased name: `1/a`, `2/ab`, `3/a/abc`, `ab/cd/abcd`.
- `/cargo/api/v1/crates/{name}/{version}/download`: the `.crate` file.
- `/cargo/api/v1/crates/new`, `.../{name}/{version}/yank` and `.../unyank`: the [registry web API](https://doc.rust-lang.org/cargo/reference/registry-web-api.html) for publishing and yanking, and `/cargo/api/v1/crates?q=` for searching.

## Mirror crates.io

A Rust feed can mirror other sparse registries. On the feed's settings page, add a mirror with the URL `https://index.crates.io` (see [Mirror](feeds.md#mirror-read-through-cache) for the settings every mirror has). Then:

- A crate's index file combines the versions published to the feed with those of the mirror, with the mirror's checksums and yanked state. Downloads go through the feed.
- A crate is downloaded from the mirror on its first use, checked against the checksum the mirror lists, and stored in the feed and served from there afterwards. A yanked version of the mirror is stored as a yanked version.
- A version published to the feed hides the same version of the mirror.
- With several mirrors, they are asked in order and the first one that has a crate lists all of its versions. A mirror that is unreachable is skipped.
- The mirror's `config.json` says where its crates are downloaded from. Credentials are only sent to the mirror's own URLs, not to download hosts such as `static.crates.io`.
- The index files of mirrors are cached in memory like the version lists of NuGet mirrors (see [Upstream listing cache](feeds.md#upstream-listing-cache)).

## In the web UI

A Rust feed lists the crates stored in it, with search, under the names they were published with. A crate's page shows the description, the readme, the dependencies, the minimum Rust version, the license, the links and keywords, the versions, a download link, and the snippets for `cargo add` and `Cargo.toml`. Crates that are only available from a mirror appear once they have been downloaded.

Users with the **delete** permission can **yank** a version, **unyank** it, or delete it for good.

## What is different from NuGet feeds

| | Rust feeds |
|---|---|
| Retention | Not available. Old versions are only removed by hand |
| Deletion behavior | Not available: Cargo yanks rather than deletes |
| Upload from the browser | Not available. Publish with `cargo publish` |
| Owners | Not available. The feed's permissions decide who may publish and yank |
| Git index | Not available. Only the sparse protocol is served |

The NuGet, pub, npm, Maven and PyPI APIs answer `404 Not Found` on a Rust feed, and the Cargo registry answers `404 Not Found` on other feeds.
