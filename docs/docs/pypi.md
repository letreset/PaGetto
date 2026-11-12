# Python packages

PaGetto can host Python packages next to NuGet, Dart, npm and Maven packages. A feed of the type **Python** is a package index: pip, uv and Poetry install from it, twine and `uv publish` upload to it, and it can mirror [PyPI](https://pypi.org) or another package index.

A feed serves one kind of package. Its type is chosen when the feed is created and can't be changed afterwards.

## Create a Python feed

On **Admin > Feeds**, press **New feed** and choose the type **Python** (see [Managing feeds](feeds.md#managing-feeds)). The feed has two URLs below `/pypi/`, clear of the pages of the web UI:

| URL | Used by |
|---|---|
| `https://your-server/feeds/py/pypi/simple/` | The **index URL**, for installing (pip, uv, Poetry) |
| `https://your-server/feeds/py/pypi/legacy/` | The **upload URL**, for publishing (twine, `uv publish`, `poetry publish`) |

The feed's **Connect** page shows both, with the settings below filled in.

## Configure the client

### pip

```ini
# pip.conf (~/.config/pip/pip.conf; %APPDATA%\pip\pip.ini on Windows)
[global]
index-url = https://your-server/feeds/py/pypi/simple/
```

pip reads the credentials from `~/.netrc` (`_netrc` on Windows), or from a keyring:

```
machine your-server
login __token__
password YOUR_TOKEN
```

A single command can also take the index on the command line: `pip install --index-url https://your-server/feeds/py/pypi/simple/ my-package`.

### uv

```toml
# pyproject.toml
[[tool.uv.index]]
name = "pagetto"
url = "https://your-server/feeds/py/pypi/simple/"
default = true
```

The credentials are the environment variables `UV_INDEX_PAGETTO_USERNAME=__token__` and `UV_INDEX_PAGETTO_PASSWORD=YOUR_TOKEN`. `default = true` replaces PyPI, which suits a feed that mirrors it; without it, uv takes a package found on this index from it alone and looks up the rest on PyPI.

### Poetry

```
poetry source add --priority=primary pagetto https://your-server/feeds/py/pypi/simple/
poetry config http-basic.pagetto __token__ YOUR_TOKEN
```

Poetry stops using PyPI once a primary source is added. For a feed that doesn't mirror PyPI, add it back with `poetry source add --priority=primary PyPI`.

### Authentication

With `Authentication:Mode` set to `Local`, `Entra` or `Hybrid`, the password is a [personal access token](authentication.md#personal-access-tokens-pats), and the username is `__token__`, the way PyPI's API tokens are used. The token owner's own username works too. Clients send the credentials with Basic authentication. The account needs the **pull** permission on the feed to install, and **push** to upload (see [Feed permissions](authentication.md#feed-permissions)).

With the `Legacy` mode, a feed without configured credentials needs no password to install. Uploading needs one of the configured API keys as the password; the username is not checked.

## Publish

Build the wheel and sdist (`python -m build` or `uv build`), then upload them. With twine, add the feed to `~/.pypirc`:

```ini
[distutils]
index-servers = pagetto

[pagetto]
repository = https://your-server/feeds/py/pypi/legacy/
username = __token__
password = YOUR_TOKEN
```

and run `twine upload --repository pagetto dist/*`. With uv:

```
uv publish --publish-url https://your-server/feeds/py/pypi/legacy/ --username __token__ --password YOUR_TOKEN
```

With Poetry, `poetry config repositories.pagetto https://your-server/feeds/py/pypi/legacy/` and then `poetry publish --repository pagetto`.

A project's name is normalized as [PEP 503](https://peps.python.org/pep-0503/) says, so `My_Package`, `my.package` and `my-package` are one project. Each version holds the files uploaded to it: an sdist and any number of wheels. PaGetto checks each upload:

- The file must be a wheel (`.whl`) or an sdist (`.tar.gz`, or `.zip` for older ones) of the project, named as the [wheel](https://packaging.python.org/en/latest/specifications/binary-distribution-format/) and [sdist](https://packaging.python.org/en/latest/specifications/source-distribution-format/) specifications say. Eggs and other old formats are refused.
- The version must be a [PEP 440](https://packaging.python.org/en/latest/specifications/version-specifiers/) version. It is stored in its normalized form, so `1.0-RC.1` is `1.0rc1`.
- The file must have its core metadata (`METADATA` in the wheel's `.dist-info` folder, `PKG-INFO` in the sdist), for the same project and version. The first file of a version gives the version its description, readme, dependencies and links.
- The `sha256_digest` and `md5_digest` the client sends must match the file.
- A file must not be larger than the feed's **Max package size**, which applies to each file.
- A file that the version already has is rejected with `409 Conflict`, unless the feed's **Package overwrite policy** allows replacing it. Uploading the same file again succeeds, since clients retry. New files can always be added to a version, as on PyPI.
- A read-only feed refuses every upload with `403 Forbidden`.

twine shows the reason of a rejected upload, for example `File already exists: my_package-1.0-py3-none-any.whl of my-package 1.0, and the feed does not allow overwriting it.`

To skip the files the feed already has, use `uv publish --check-url https://your-server/feeds/py/pypi/simple/`. twine 6 and later only allow `--skip-existing` for PyPI and TestPyPI.

## The package index

The index is the [simple repository API](https://packaging.python.org/en/latest/specifications/simple-repository-api/), in its HTML form ([PEP 503](https://peps.python.org/pep-0503/)) and its JSON form ([PEP 691](https://peps.python.org/pep-0691/), API version 1.1), chosen by the client's `Accept` header:

- `/pypi/simple/` lists the projects stored in the feed. Projects that only a mirror has are not listed.
- `/pypi/simple/{project}/` lists the files of a project, each with its SHA-256, `requires-python`, size and upload time. A URL with a name that is not normalized redirects to the normalized one.
- The core metadata of each wheel is served next to it ([PEP 658](https://peps.python.org/pep-0658/)), so pip and uv resolve dependencies without downloading whole wheels.
- An unlisted version is **yanked** ([PEP 592](https://peps.python.org/pep-0592/)): installers only pick it when it is pinned exactly, for example `my-package==1.0`.

## Mirror PyPI

A Python feed can mirror other package indexes. On the feed's settings page, add a mirror with the URL `https://pypi.org/simple` (see [Mirror](feeds.md#mirror-read-through-cache) for the settings every mirror has). Then:

- A project's page combines the files published to the feed with those of the mirror, with the mirror's hashes, `requires-python` and yanked state. Its file URLs point at the feed.
- A file is downloaded from the mirror on its first use, checked against the SHA-256 the mirror lists, stored in the feed with the mirror's upload time and served from there afterwards. A yanked file of the mirror is stored as an unlisted version.
- A version published to the feed hides the same version of the mirror.
- With several mirrors, they are asked in order and the first one that has a project lists all of its files. A mirror that is unreachable is skipped.
- Mirrors are asked for the JSON form of the API and may answer with the HTML form, so other servers such as devpi, Artifactory or Nexus work too. Credentials are only sent to the mirror's own host, not to file hosts such as `files.pythonhosted.org`.
- The project pages of mirrors are cached in memory like the version lists of NuGet mirrors (see [Upstream listing cache](feeds.md#upstream-listing-cache)).

## In the web UI

A Python feed lists the packages stored in it, with search, under the names they were published with. A package's page shows the summary, the readme, the requirements, the supported Python versions, the license, the links and keywords, the files of the version with their SHA-256, the versions, and the commands for pip, uv, Poetry and `requirements.txt`. Packages that are only available from a mirror appear once a file has been downloaded.

Users with the **delete** permission can **yank** a version, **unyank** it, or delete it with its files for good.

## What is different from NuGet feeds

| | Python feeds |
|---|---|
| Retention | Not available. Old versions are only removed by hand |
| Deletion behavior | Not available: the upload API has no delete |
| Upload from the browser | Not available. Upload with twine, uv or Poetry |
| Search | Only the web UI searches; the simple API has no search (`pip search` was removed from PyPI too) |
| Formats | Wheels and sdists only; no eggs |

The NuGet, pub, npm and Maven APIs answer `404 Not Found` on a Python feed, and the package index answers `404 Not Found` on other feeds.
