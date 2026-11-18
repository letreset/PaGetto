# Hex packages

PaGetto can host Elixir and Erlang packages next to NuGet, Dart, npm, Maven, Python, Rust, Go and Ruby packages. A feed of the type **Hex** is a Hex repository: Mix and Rebar3 install packages from it, `mix hex.publish` and `mix hex.retire` work against it, and it can mirror [hex.pm](https://hex.pm) or another Hex repository.

A feed serves one kind of package. Its type is chosen when the feed is created and can't be changed afterwards.

## Create a Hex feed

On **Admin > Feeds**, press **New feed** and choose the type **Hex** (see [Managing feeds](feeds.md#managing-feeds)). The repository is below `/hex/`, clear of the pages of the web UI:

```
https://your-server/feeds/elixir/hex
```

The feed's **Connect** page shows the repository URL, the repository name and the fingerprint of the feed's public key, with the settings below filled in.

### The repository name and the signed registry

A Hex repository signs its registry, and clients check every registry file against the repository's public key. Each Hex feed has its own RSA key, created the first time the feed is used. The private key is kept in the package storage, encrypted with the [Data Protection keys](upgrading.md#data-protection-keys), which are kept there too. Back up the storage, and the Data Protection certificate if you configured one: if the key is lost, clients have to be given the new public key. Deleting a Hex feed deletes its key, so a feed created later with the same slug has a new key, which clients of the deleted feed don't accept.

The public key is at `/hex/public_key` of the feed. Clients also check that the registry is the one of the repository they configured, so a feed is a repository named after its slug: add it under that name.

## Configure Mix

Add the feed as a repository. The examples use a feed with the slug `elixir`:

```bash
mix hex.repo add elixir https://your-server/feeds/elixir/hex \
  --fetch-public-key SHA256:... \
  --auth-key YOUR_TOKEN
```

`--fetch-public-key` downloads the public key and checks it against the fingerprint from the Connect page. `--public-key key.pem` takes a key saved before instead. Mix stores the repository, its key and the token in `~/.hex/hex.config`.

A dependency names the repository it comes from:

```elixir
{:my_package, "~> 1.0", repo: "elixir"}
```

The dependencies of a package from the feed come from the feed as well, unless the package names another repository for them. Packages published with Mix name `hexpm` for their dependencies from hex.pm.

### Use the feed in place of hex.pm

To get every package through a feed that [mirrors hex.pm](#mirror-hexpm), without `repo:` in `mix.exs`, point the `hexpm` repository at the feed:

```bash
mix hex.repo set hexpm --url https://your-server/feeds/elixir/hex \
  --public-key elixir.pem --auth-key YOUR_TOKEN --no-oauth-exchange
export HEX_NO_VERIFY_REPO_ORIGIN=1
```

`--no-oauth-exchange` keeps Mix from trading the token for a hex.pm token. The feed's registry is named `elixir`, not `hexpm`, so the name check has to be turned off with `HEX_NO_VERIFY_REPO_ORIGIN`; the signature is still checked. Mix then prints `Failed to check for new Hex version`, because the feed doesn't serve Hex's own installs list; that is harmless. `mix hex.repo remove hexpm` goes back to hex.pm.

## Configure Rebar3

Add the repository to `rebar.config`, in the project or in `~/.config/rebar3/rebar.config`, with the public key from `/hex/public_key`:

```erlang
{hex, [
  {repos, [
    #{name => <<"elixir">>,
      repo_url => <<"https://your-server/feeds/elixir/hex">>,
      repo_key => <<"YOUR_TOKEN">>,
      repo_public_key => <<"-----BEGIN PUBLIC KEY-----
...
-----END PUBLIC KEY-----">>}
  ]}
]}.
```

Rebar3 looks for packages in its repositories in order, hex.pm last. `{repos, replace, [...]}` leaves hex.pm out, for a feed that mirrors it.

### Authentication

With `Authentication:Mode` set to `Local`, `Entra` or `Hybrid`, the token is a [personal access token](authentication.md#personal-access-tokens-pats). Mix and Rebar3 send it as the whole `Authorization` header, without a scheme, with every request to the repository. The account needs the **pull** permission on the feed to install, **push** to publish, and **delete** to retire (see [Feed permissions](authentication.md#feed-permissions)).

With the `Legacy` mode, a feed without configured credentials needs no token to read: leave out `--auth-key` and `repo_key`. Publishing and retiring need one of the configured API keys. Hex can't send a username and password, so a `Legacy` server with configured credentials can't serve Hex.

## Publish

Publish with the feed's API, which is at `/hex/api` of the feed, and a token:

```bash
export HEX_API_URL=https://your-server/feeds/elixir/hex/api
export HEX_API_KEY=YOUR_TOKEN
mix hex.publish package
```

`mix hex.publish` without `package` builds and publishes the documentation as well, which the feed doesn't host; use `mix hex.publish package`. PaGetto checks each package:

- The tarball must be a Hex package tarball (format 3) whose `CHECKSUM` matches its content, as `mix hex.build` writes it.
- The name must be lowercase ASCII letters, digits and `_`, start with a letter and have at most 128 characters, as on hex.pm.
- The version must be a [semantic version](https://semver.org). Versions that differ only in their build metadata (`1.0.0+a` and `1.0.0+b`) are the same version.
- The tarball must not be larger than the feed's **Max package size**.
- A version that the feed already has is rejected with `409 Conflict`. When the feed's **Package overwrite policy** allows replacing it, `mix hex.publish package --replace` replaces it.
- A read-only feed refuses every publish and retirement with `403 Forbidden`.

Mix shows the reason the feed gives, for example `Version 1.0.0 of package my_package already exists.`

The `README.md` of the package is shown on its page.

### Retire

`mix hex.retire my_package 1.0.0 deprecated --message "Use 1.0.1"` retires a version, with one of the reasons `other`, `invalid`, `security`, `deprecated` and `renamed`, and `mix hex.retire my_package 1.0.0 --unretire` takes it back. A retired version stays in the registry and can still be downloaded; Mix shows the reason and message when it resolves it. Retiring needs the **delete** permission, as unlisting does on other feeds.

Reverting a release (`mix hex.publish --revert`), documentation, owners and organizations are not supported: permissions come from the feed.

## The repository

The feed answers what Hex clients ask a repository ([registry v2](https://github.com/hexpm/specifications/blob/main/registry-v2.md)):

- `/hex/names` and `/hex/versions`: the packages stored in the feed, and their versions with the retired ones marked.
- `/hex/packages/{name}`: the versions of a package with their checksums, dependencies and retirement.
- `/hex/tarballs/{name}-{version}.tar`: the package tarball.
- `/hex/public_key`: the key that checks the signature of the registry.

The registry files are gzipped protocol buffers, signed with the feed's key. The API at `/hex/api/` takes `mix hex.publish` (`POST /packages/{name}/releases`) and `mix hex.retire` (`POST` and `DELETE /packages/{name}/releases/{version}/retire`).

## Mirror hex.pm

A Hex feed can mirror other Hex repositories. On the feed's settings page, add a mirror with the URL `https://repo.hex.pm` (see [Mirror](feeds.md#mirror-read-through-cache) for the settings every mirror has). Then:

- A package's registry file combines the versions published to the feed with those of the mirror, with the mirror's checksums, dependencies and retirement, signed with the feed's key. Downloads go through the feed.
- A tarball is downloaded from the mirror on its first use, checked against the checksum the mirror lists, and stored in the feed and served from there afterwards. A retired version of the mirror is stored as a retired version.
- The dependencies of a mirrored package are listed as they are on the mirror. Those of hex.pm name no repository, so Mix and Rebar3 get them from the feed too.
- A version published to the feed hides the same version of the mirror.
- With several mirrors, they are asked in order and the first one that has a package lists all of its versions. A mirror that is unreachable is skipped.
- `/hex/names` and `/hex/versions` only list the packages stored in the feed. Mix and Rebar3 ask for each package by name, so they find the mirror's packages anyway.
- The registry files of mirrors are cached in memory like the version lists of NuGet mirrors (see [Upstream listing cache](feeds.md#upstream-listing-cache)).

## In the web UI

A Hex feed lists the packages stored in it, with search. A package's page shows the description, the readme, the dependencies, the Elixir version, the licenses, the links, the versions, a download link, and the snippets for `mix.exs` and `rebar.config`. Packages that are only available from a mirror appear once they have been downloaded.

Users with the **delete** permission can **retire** a version (with the reason `other`), **unretire** it, or delete it for good.

## What is different from NuGet feeds

| | Hex feeds |
|---|---|
| Retention | The feed's own version limits, applied within an hour, and the removal of unused mirrored versions. See [auto-deletion](configuration.md#package-auto-deletion) |
| Deletion behavior | Not available: Hex retires rather than deletes |
| Upload from the browser | Not available. Publish with `mix hex.publish package` |
| Documentation | Not hosted. Publish packages without docs |
| Owners and organizations | Not available. The feed's permissions decide who may publish and retire |

The NuGet, pub, npm, Maven, PyPI, Cargo, Go and RubyGems APIs answer `404 Not Found` on a Hex feed, and the Hex repository answers `404 Not Found` on other feeds.
