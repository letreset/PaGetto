# R packages

A feed of the type **R** is a [CRAN-like repository](https://cran.r-project.org/doc/manuals/r-release/R-admin.html#Setting-up-a-package-repository) of R source packages: `install.packages` and renv install packages from it, source packages are uploaded to it with a plain HTTP request, and it can mirror [CRAN](https://cran.r-project.org) or another CRAN-like repository.

A feed serves one kind of package. Its type is chosen when the feed is created and can't be changed afterwards.

## Create an R feed

On **Admin > Feeds**, press **New feed** and choose the type **R** (see [Managing feeds](feeds.md#managing-feeds)). The repository is below `/cran`, clear of the pages of the web UI. R is configured with the **Repository URL**:

```
https://your-server/feeds/r/cran
```

The feed's **Connect** page shows it, with the settings below filled in.

## Configure R

Give the repository URL to `install.packages` as `repos`, or set it for every session in `.Rprofile`:

```r
options(repos = c(
  r = "https://your-server/feeds/r/cran",
  CRAN = "https://cloud.r-project.org"
))
```

R reads the index of every repository of `repos`, and installs a package from the repository with its newest version. Without a [mirror](#mirror-cran), public packages still come from CRAN. With a mirror of CRAN, leave CRAN out to get every package through the feed:

```r
options(repos = c(r = "https://your-server/feeds/r/cran"))
install.packages("mypackage")
```

[renv](https://rstudio.github.io/renv/) uses the same repositories. It also installs older versions, which it downloads from the feed's archive:

```r
renv::install("mypackage@1.0.0")
```

`remotes::install_version` only finds the current version: it looks for older ones in `src/contrib/Meta/archive.rds`, which the feed doesn't serve.

### Binary packages

The feed serves source packages only. R on Windows and macOS asks for binary packages first; the feed answers with an empty index for them, so R installs the source package. Packages with compiled code then need the build tools: [Rtools](https://cran.r-project.org/bin/windows/Rtools/) on Windows, the Xcode command line tools on macOS.

### Authentication

R has no setting for the credentials of a repository: it sends those of the repository URL, with Basic authentication. Keep the token out of scripts and `.Rprofile`, e.g. in an environment variable set in `.Renviron`:

```r
options(repos = c(
  r = sprintf("https://__token__:%s@your-server/feeds/r/cran", Sys.getenv("PAGETTO_TOKEN"))
))
```

With `Authentication:Mode` set to `Local`, `Entra` or `Hybrid`, the password is a [personal access token](authentication.md#personal-access-tokens-pats), and the username is `__token__` or the token owner's username. The account needs the **pull** permission on the feed to install, and **push** to upload (see [Feed permissions](authentication.md#feed-permissions)).

With the `Legacy` mode, a feed without configured credentials needs no password to read. Uploading needs one of the configured API keys as the password; the username is not checked.

renv writes the repositories into `renv.lock` without the credentials of their URLs, so `renv::restore()` would ask the feed without them. Let renv restore from the repositories of `options(repos)` instead, in `.Rprofile` after setting them:

```r
options(renv.config.repos.override = getOption("repos"))
```

## Upload

R has no command to publish a package: CRAN takes submissions by hand. A source package is uploaded to the feed with `PUT` to its path below `src/contrib/`, as Nexus Repository takes it, which needs the **push** permission:

```bash
R CMD build mypackage
curl --fail-with-body -u __token__:<token> -T mypackage_1.0.0.tar.gz \
  https://your-server/feeds/r/cran/src/contrib/
```

`curl -T` adds the file name to a URL that ends with `/`. PaGetto checks each upload:

- The package must be a gzipped tar archive as `R CMD build` writes it: its files in a folder named after the package, with the package's `DESCRIPTION` file in it.
- `DESCRIPTION` must have a valid `Package` name (letters, digits and dots, at least two characters, starting with a letter and not ending with a dot) and `Version` (two or more numbers separated by `.` or `-`, such as `1.0.2` or `0.9-12`). Names are compared regardless of case, as CRAN does, and keep the case they were uploaded with. Versions are compared by their numbers, so `1.0-2` and `1.0.2` are the same version.
- The file name must be `{name}_{version}.tar.gz` of the package's `DESCRIPTION`.
- The package must not be larger than the feed's **Max package size**.
- A version that the feed already has is rejected with `409 Conflict`, unless the feed's **Package overwrite policy** is **Allow all**. R has no prerelease versions, so **Prerelease only** never replaces one.
- A read-only feed refuses every upload with `403 Forbidden`.

The feed answers `201 Created`, or an error with the reason as plain text, which `curl --fail-with-body` prints.

## The repository

The feed answers the requests of a CRAN-like repository:

- `/cran/src/contrib/PACKAGES` and `PACKAGES.gz`: the index, with the current version of each package: its newest listed version. Each entry has the dependency fields of the package's `DESCRIPTION` (`Depends`, `Imports`, `LinkingTo`, `Suggests`, `Enhances`), its `License`, `NeedsCompilation` and the `MD5sum` of the package, which R checks. `PACKAGES.rds` is not served; R then reads `PACKAGES.gz`.
- `/cran/src/contrib/{name}_{version}.tar.gz`: a source package. Every version is served at this path.
- `/cran/src/contrib/Archive/{name}/`: the versions of a package other than the current one, as a directory listing, and `/cran/src/contrib/Archive/{name}/{name}_{version}.tar.gz`, where renv downloads them.
- `/cran/bin/.../PACKAGES` and `PACKAGES.gz`: empty indexes of binary packages.

An unknown package is `404 Not Found`. An unlisted version is left out of the index and the archive, but it can still be downloaded, so lock files keep working.

## Mirror CRAN

An R feed can mirror other CRAN-like repositories. On the feed's settings page, add a mirror with the URL `https://cloud.r-project.org`, or that of another [CRAN mirror](https://cran.r-project.org/mirrors.html) (see [Mirror](feeds.md#mirror-read-through-cache) for the settings every mirror has). Then:

- The index combines the packages uploaded to the feed with those of the mirror's `src/contrib/PACKAGES.gz`. For each package it lists the newest version: the feed's, or the mirror's when it is newer. A version of the feed hides the mirror's version of the same number, and a version unlisted in the feed is left out even when the mirror has it.
- The archive of a package combines the feed's versions with the mirror's `src/contrib/Archive/{name}/`.
- A version is downloaded from the mirror when it is first asked for, checked against the MD5 of the mirror's index, stored in the feed and served from there afterwards. Only source packages are mirrored.
- With several mirrors, they are asked in order: the first one whose index has a package serves it. A mirror that is unreachable is skipped.
- The index and the archive listings of mirrors are cached in memory, as on every feed type (see [Upstream listing cache](feeds.md#upstream-listing-cache)). CRAN's index has more than 20,000 packages, so the feed's index is a few MiB.

## In the web UI

An R feed lists the packages stored in it, with search. A package's page shows the readme (`README.md` of the package), its title, the R versions it supports, the packages it depends on, its license, maintainer and links, the versions, a download link, and the snippets for `install.packages` and renv. Packages that are only available from a mirror appear once they have been downloaded.

Users with the **delete** permission can **unlist** a version, which leaves it out of the index while it can still be downloaded, **relist** it, or delete it for good.

## Feed settings and limits

Most [feed settings](feeds.md#feed-settings) apply to R feeds as to any other feed. These differ:

| | R feeds |
|---|---|
| Retention | The feed's own version limits, applied within an hour, and the removal of unused mirrored versions. See [auto-deletion](configuration.md#package-auto-deletion) |
| Deletion behavior | Not available |
| Upload from the browser | Not available. Upload with `curl` or another HTTP client |
| Binary packages | Not hosted or mirrored |
| `PACKAGES.rds`, `Meta/archive.rds`, CRAN's package checks and web pages | Not served |

The APIs of other feed types answer `404 Not Found` on an R feed, and the CRAN repository answers `404 Not Found` on feeds of other types.
