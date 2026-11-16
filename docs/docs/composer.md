# PHP packages

PaGetto can host PHP packages next to NuGet, Dart, npm, Maven, Python, Rust, Go, Ruby and Hex packages. A feed of the type **PHP** is a [Composer repository](https://getcomposer.org/doc/05-repositories.md#composer): Composer installs packages from it, package zips are uploaded to it with a plain HTTP request, and it can mirror [Packagist](https://packagist.org) or another Composer repository.

A feed serves one kind of package. Its type is chosen when the feed is created and can't be changed afterwards.

## Create a PHP feed

On **Admin > Feeds**, press **New feed** and choose the type **PHP** (see [Managing feeds](feeds.md#managing-feeds)). The repository is below `/composer`, clear of the pages of the web UI. Composer is configured with the **Repository URL**:

```
https://your-server/feeds/php/composer
```

The feed's **Connect** page shows it, with the settings below filled in.

## Configure Composer

Add the feed to the `repositories` of your project's `composer.json`, or let Composer do it:

```bash
composer config repositories.php composer https://your-server/feeds/php/composer
```

```json
{
  "repositories": [
    {"type": "composer", "url": "https://your-server/feeds/php/composer"}
  ]
}
```

Composer asks the repositories of `composer.json` before Packagist, and takes a package from the first repository that has it. Without a [mirror](#mirror-packagist), public packages still come from Packagist. With a mirror of Packagist, turn Packagist off to get every package through the feed:

```bash
composer config repo.packagist false
```

Then use the packages as usual: `composer require acme/hello:^1.0`, or a `require` in `composer.json`. PaGetto serves the [Composer 2](https://getcomposer.org/doc/05-repositories.md#metadata-url) repository format; Composer 1 is not supported.

Composer refuses repositories at an `http://` URL. For a test server without HTTPS, allow them with `composer config secure-http false`.

### Authentication

Composer sends the credentials of a server's host from [`auth.json`](https://getcomposer.org/doc/articles/authentication-for-private-packages.md#http-basic) with Basic authentication (`http-basic`):

```bash
composer config --global http-basic.your-server __token__ <token>
```

```json
{
  "http-basic": {
    "your-server": {"username": "__token__", "password": "<token>"}
  }
}
```

The key is the host of the repository URL, with its port when the URL has one: `http-basic.your-server:8080` for `https://your-server:8080/...`. An `auth.json` next to `composer.json` works too; keep it out of version control. Composer also reads the `COMPOSER_AUTH` environment variable, which takes the same JSON and suits CI builds.

With `Authentication:Mode` set to `Local`, `Entra` or `Hybrid`, the password is a [personal access token](authentication.md#personal-access-tokens-pats), and the username is `__token__` or the token owner's username. The account needs the **pull** permission on the feed to install, and **push** to upload (see [Feed permissions](authentication.md#feed-permissions)).

With the `Legacy` mode, a feed without configured credentials needs no password to read. Uploading needs one of the configured API keys as the password; the username is not checked.

## Upload

Composer has no command to publish a package: Packagist reads packages from version control. A package zip is uploaded to the feed with `PUT`, with the package name and the version in the URL, which needs the **push** permission. The URL is the one Nexus Repository uses for Composer uploads:

```bash
composer archive --format=zip --file=package
curl --fail-with-body -u __token__:<token> -T package.zip \
  https://your-server/feeds/php/composer/packages/upload/acme/hello/1.0.0
```

`composer archive` leaves out the files that the `archive.exclude` of `composer.json` and `.gitattributes` (`export-ignore`) exclude. A zip from `git archive` works too, with or without a `--prefix` folder.

PaGetto checks each upload:

- The name must be a valid package name, `vendor/package` of letters and digits separated by `_`, `.` or `-`, at most 128 characters. Names are compared regardless of case, and stored lowercase.
- The version must be one Composer accepts, such as `1.0.0`, `v1.2.3`, `2.0.0-beta1`, `dev-main` or `1.x-dev`, of ASCII letters, digits and `._+-`. Branch names with a slash can't be uploaded. Versions are compared in [Composer's normalized form](https://getcomposer.org/doc/articles/versions.md), so `v1.0.0` and `1.0` are the same version.
- The zip must have `composer.json` at its root, or in its only folder when it has nothing else at its top level (Composer installs the package from that folder). Its `name` must be the uploaded package's, and a `version`, which `composer.json` usually doesn't have, must be the uploaded version.
- The zip must not be larger than the feed's **Max package size**.
- A tagged version that the feed already has is rejected with `409 Conflict`, unless the feed's **Package overwrite policy** allows replacing it. A development version such as `dev-main` follows a branch, so it is always replaced, like a Maven snapshot.
- A read-only feed refuses every upload with `403 Forbidden`.

The feed answers `201 Created`, or an error with the reason as plain text, which `curl --fail-with-body` prints.

## The repository

The feed answers the requests of the [Composer 2 repository format](https://getcomposer.org/doc/05-repositories.md#composer):

- `/composer/packages.json`: the root, whose `metadata-url` points Composer at the metadata of each package.
- `/composer/p2/{vendor}/{package}.json`: the tagged versions of a package, newest first, each with its `composer.json` data, `version`, `version_normalized`, `time` and `dist`.
- `/composer/p2/{vendor}/{package}~dev.json`: the development versions, which Composer only reads when a constraint or the minimum stability allows them.
- `/composer/dists/{vendor}/{package}/{version}/{reference}.zip`: the zip of a version. The `dist` of an uploaded version has the zip's SHA-1 as its `shasum` and its `reference`, so a replaced version gets a new URL and Composer installs it again.

An unknown package is `404 Not Found`. An unlisted version is left out of the metadata, but its zip can still be downloaded, so lock files keep working.

## Mirror Packagist

A PHP feed can mirror other Composer repositories that serve their metadata at `p2/` below their URL. On the feed's settings page, add a mirror with the URL `https://repo.packagist.org` (see [Mirror](feeds.md#mirror-read-through-cache) for the settings every mirror has). Then:

- A package's metadata combines the versions uploaded to the feed with those of the mirror. A version of the feed hides the mirror's version of the same normalized form, and a version unlisted in the feed is left out even when the mirror has it.
- The tagged versions of the mirror are listed with a `dist` that points at the feed. A version is downloaded when its zip is first asked for, from the `dist` URL of the mirror's metadata, stored in the feed with the mirror's metadata and reference, and served from there afterwards. For Packagist, that URL is the package's code host, such as GitHub, which the server must be able to reach. The mirror's credentials are only sent when the zip is on the mirror's own host.
- Development versions of the mirror change with their branch, so they are not stored: the `~dev` metadata lists them as the mirror does, and Composer downloads them from the mirror's `dist` URL itself.
- With several mirrors, they are asked in order: the first one that has a package lists all of its versions. A mirror that is unreachable is skipped.
- The metadata of mirrors is cached in memory like the version lists of NuGet mirrors (see [Upstream listing cache](feeds.md#upstream-listing-cache)).

## In the web UI

A PHP feed lists the packages stored in it, with search. A package's page shows the readme (`README.md` of the zip), the PHP versions it supports, the packages it requires, its license, keywords and links, the versions, a download link, and the snippets for `composer require` and `composer.json`. Packages that are only available from a mirror appear once a zip has been downloaded.

Users with the **delete** permission can **unlist** a version, which leaves it out of the metadata while it can still be downloaded, **relist** it, or delete it for good.

## What is different from NuGet feeds

| | PHP feeds |
|---|---|
| Retention | Not available. Old versions are only removed by hand |
| Deletion behavior | Not available |
| Upload from the browser | Not available. Upload with `curl` or another HTTP client |
| Abandoned packages, search and statistics of Packagist | Not served |
| Direct access to version control | Not available. Packages come from uploads and mirrors |

The NuGet, pub, npm, Maven, PyPI, Cargo, Go, RubyGems and Hex APIs answer `404 Not Found` on a PHP feed, and the Composer repository answers `404 Not Found` on other feeds.
