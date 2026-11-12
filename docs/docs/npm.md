# npm packages

PaGetto can host JavaScript and TypeScript packages next to NuGet and Dart packages. A feed of the type **npm** is an npm registry: npm, Yarn and pnpm install from it and publish to it, and it can mirror [registry.npmjs.org](https://registry.npmjs.org) or another npm registry.

A feed serves one kind of package. Its type is chosen when the feed is created and can't be changed afterwards.

## Create an npm feed

On **Admin > Feeds**, press **New feed** and choose the type **npm** (see [Managing feeds](feeds.md#managing-feeds)). The feed's **registry URL** is its address followed by `/npm/`:

```
https://your-server/feeds/js/npm/
```

The registry lives below `/npm/` so that package names can't collide with the pages of the web UI: there are npm packages called `connect` and `statistics`. The feed's **Connect** page shows the registry URL with the settings below filled in.

## Configure the client

npm and pnpm read `.npmrc`, next to `package.json` or in your home folder. Point the registry at the feed, and give the token for it:

```ini
registry=https://your-server/feeds/js/npm/
//your-server/feeds/js/npm/:_authToken=${NPM_TOKEN}
```

To take only the packages of one scope from the feed, and everything else from npmjs.org, set the registry for the scope instead:

```ini
@my-scope:registry=https://your-server/feeds/js/npm/
//your-server/feeds/js/npm/:_authToken=${NPM_TOKEN}
```

Yarn 1 reads `.npmrc` too. Yarn 2 and later read `.yarnrc.yml`:

```yaml
npmRegistryServer: "https://your-server/feeds/js/npm/"
npmAlwaysAuth: true
npmAuthToken: "${NPM_TOKEN}"
```

Yarn 2 and later refuse plain HTTP registries other than those in `unsafeHttpWhitelist`.

### Authentication

With `Authentication:Mode` set to `Local`, `Entra` or `Hybrid`, the token is a [personal access token](authentication.md#personal-access-tokens-pats). The clients send it as a bearer token. The account needs the **pull** permission on the feed to install, **push** to publish, deprecate and change dist-tags, and **delete** to unpublish (see [Feed permissions](authentication.md#feed-permissions)).

`npm login` is not supported: put the token in the configuration as shown above. `npm whoami` shows the account the token belongs to.

With the `Legacy` mode, a feed without configured credentials needs no token to install. Changes need one of the configured API keys as the token.

## Publish a package

```shell
npm publish --registry https://your-server/feeds/js/npm/
```

Or set the registry once in `package.json`:

```json
"publishConfig": { "registry": "https://your-server/feeds/js/npm/" }
```

PaGetto checks the package before it accepts it:

- The name must be a valid, lowercase npm package name of at most 128 characters, scoped (`@scope/name`) or not.
- The tarball must be a gzipped tar file with a `package.json` whose name and version match the ones published, of at most 1 GiB once decompressed, and it must match the integrity hash the client sent.
- The tarball must not be larger than the feed's **Max package size**.
- A version that already exists is rejected with `409 Conflict`, unless the feed's **Package overwrite policy** allows replacing it.
- A read-only feed refuses every change with `403 Forbidden`.

## Change a package

| Command | What happens | Permission |
|---|---|---|
| `npm dist-tag add pkg@1.2.3 next`, `npm dist-tag rm pkg next`, `npm dist-tag ls pkg` | Adds, moves, removes or lists a tag. A tag starts with a letter, and `latest` can't be removed | push |
| `npm deprecate pkg@1.2.3 "message"` | Deprecates the version: it is unlisted, and clients show the message when they install it. An empty message undeprecates it | push |
| `npm unpublish pkg@1.2.3` | Removes the version and its file for good, and the tags that point at it | delete |
| `npm unpublish pkg --force` | Removes every version of the package | delete |

A package without a `latest` tag gets one: the newest stable version that isn't deprecated.

## Mirror registry.npmjs.org

An npm feed can mirror other npm registries. On the feed's settings page, add a mirror with the URL `https://registry.npmjs.org` (see [Mirror](feeds.md#mirror-read-through-cache) for the settings every mirror has). Then:

- A package's document combines the versions published to the feed with those of the mirror. A version published to the feed hides the same version of the mirror, and so does a dist-tag.
- A version is downloaded from the mirror on its first use, checked against the integrity hash the mirror lists, stored in the feed and served from there afterwards.
- With several mirrors, they are asked in order and the first one that has the package serves all of its versions. A mirror that is unreachable is skipped.
- Package documents from mirrors are cached in memory like the version lists of NuGet mirrors (see [Upstream listing cache](feeds.md#upstream-listing-cache)).

A mirror's credentials are only sent to the mirror itself, not to another host its tarballs are served from.

:::tip

With a mirror, set the feed as the `registry` for everything. Without one, use a scoped registry, so that public packages still come from npmjs.org.

:::

## In the web UI

An npm feed lists the packages stored in it, with search. A package's page shows its readme, dependencies, `engines`, keywords, license and links, the versions, and the install commands for npm, Yarn and pnpm. Packages that are only available from a mirror appear once a version has been downloaded.

Users with the **delete** permission can **deprecate** a version (without a message of its own, clients show "This version is deprecated."), **undeprecate** it, or delete it for good.

Package names are stored in lowercase, so an older npmjs.org package with capitals in its name, such as `JSONStream`, is listed as `jsonstream`.

## What is different from NuGet feeds

| | npm feeds |
|---|---|
| Retention | Not available. Old versions are only removed by hand |
| Deletion behavior | Not available: `npm unpublish` always removes the version |
| Upload from the browser | Not available. Publish with `npm publish` |
| `npm login`, `npm audit`, signatures | Not served. `npm install` carries on without the audit |
| Search | `npm search` finds the packages stored in the feed, not those only a mirror has |

The NuGet and pub APIs answer `404 Not Found` on an npm feed, and the npm API answers `404 Not Found` on other feeds.
