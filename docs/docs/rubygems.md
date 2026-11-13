# Ruby gems

PaGetto can host Ruby gems next to NuGet, Dart, npm, Maven, Python and Rust packages and Go modules. A feed of the type **Ruby** is a gem server with the [compact index](https://guides.rubygems.org/rubygems-org-compact-index-api/) that Bundler and RubyGems read: `bundle install` and `gem install` install gems from it, `gem push` and `gem yank` publish and yank them, and it can mirror [rubygems.org](https://rubygems.org) or another gem server.

A feed serves one kind of package. Its type is chosen when the feed is created and can't be changed afterwards.

## Create a Ruby feed

On **Admin > Feeds**, press **New feed** and choose the type **Ruby** (see [Managing feeds](feeds.md#managing-feeds)). The gem server is below `/rubygems/`, clear of the pages of the web UI. Clients are configured with the **gem source URL**:

```
https://your-server/feeds/gems/rubygems/
```

The feed's **Connect** page shows it, with the settings below filled in.

## Install gems with Bundler

Name the feed as a source in the `Gemfile`. With a [mirror of rubygems.org](#mirror-rubygemsorg), the feed can be the only source:

```ruby
source "https://your-server/feeds/gems/rubygems/"

gem "my_gem"
```

Without a mirror, keep rubygems.org as the main source and get the feed's gems from a source block:

```ruby
source "https://rubygems.org"

source "https://your-server/feeds/gems/rubygems/" do
  gem "my_gem"
end
```

Or add a gem with `bundle add my_gem --source "https://your-server/feeds/gems/rubygems/"`.

## Install gems with gem

```bash
gem install my_gem --source https://your-server/feeds/gems/rubygems/
```

`gem sources --add https://your-server/feeds/gems/rubygems/` adds the feed to the sources of every `gem install`.

### Authentication

Bundler and gem send Basic credentials. Store them for Bundler once per machine (or in `BUNDLE_<HOST>` environment variables in CI), keyed by the server's host:

```bash
bundle config set --global your-server __token__:<token>
```

gem takes them from the source URL itself: `https://__token__:<token>@your-server/feeds/gems/rubygems/`.

With `Authentication:Mode` set to `Local`, `Entra` or `Hybrid`, the password is a [personal access token](authentication.md#personal-access-tokens-pats), and the username is `__token__` or the token owner's username. The account needs the **pull** permission on the feed to install, **push** to push and **delete** to yank (see [Feed permissions](authentication.md#feed-permissions)).

With the `Legacy` mode, a feed without configured credentials needs no password to read. Pushing and yanking need one of the configured API keys. gem sends the API key as the whole `Authorization` header, so a `Legacy` server with configured credentials can only be read by Bundler and gem, not pushed to.

## Push and yank

`gem push` takes the feed's gem source URL without the trailing slash as its host, and the API key from `GEM_HOST_API_KEY` (or `~/.gem/credentials`). The API key is a personal access token:

```bash
export GEM_HOST_API_KEY=<token>
gem build my_gem.gemspec
gem push my_gem-1.0.0.gem --host https://your-server/feeds/gems/rubygems
```

A gemspec with `allowed_push_host` in its metadata can only be pushed to that host, so set it to the same URL. Each platform of a version is pushed as its own gem file, such as `my_gem-1.0.0.gem` and `my_gem-1.0.0-x86_64-linux.gem`.

PaGetto checks each push:

- The file must be a gem: a tar archive with the gemspec in `metadata.gz`.
- The name must be letters, digits, `.`, `-` and `_`, with at least one letter, and at most 128 characters. A gem keeps the name it was first pushed under: names that differ only in case are the same gem, as on rubygems.org.
- The version must be a gem version such as `1.2.3` or `2.0.0.rc1`, and the platform one such as `java` or `x86_64-linux`.
- The gem must not be larger than the feed's **Max package size**.
- A gem file the feed already has is accepted again when it is the same file. With other content it is rejected with `409 Conflict`, unless the feed's **Package overwrite policy** allows replacing it. Bundler records the checksum of each gem in `Gemfile.lock` and stops when it gets other content.
- A read-only feed refuses every push and yank with `403 Forbidden`.

The feed answers like rubygems.org, with a message that `gem push` prints.

`gem yank my_gem -v 1.0.0 --host https://your-server/feeds/gems/rubygems` yanks a version: it is left out of the compact index, so Bundler and gem no longer choose it, while a `Gemfile.lock` that names it can still download it. A yank applies to the version with all its platforms; a `--platform` must name one the version has. A yanked version can be restored on its package page.

## The gem server

The feed answers the requests of Bundler and RubyGems:

- `/rubygems/versions`: every gem with its versions and the checksum of its info file.
- `/rubygems/info/{gem}`: a line per gem file, with its runtime dependencies, its SHA-256 and the Ruby and RubyGems versions it needs.
- `/rubygems/names`: the gems of the feed.
- `/rubygems/gems/{gem}-{version}[-{platform}].gem`: a gem file.
- `/rubygems/quick/Marshal.4.8/{gem}-{version}[-{platform}].gemspec.rz`: the gemspec of a gem file, which `gem install` reads before it downloads the gem.
- `POST /rubygems/api/v1/gems` and `DELETE /rubygems/api/v1/gems/yank`: `gem push` and `gem yank`.

The files of the compact index answer `If-None-Match` and range requests, so Bundler only downloads what changed. The full index of old RubyGems versions (`specs.4.8.gz`) and the dependency API are not served: Bundler 2 and RubyGems 3 use the compact index. `gem search --remote` and `gem list --remote` need the full index, so they don't work with a Ruby feed.

## Mirror rubygems.org

A Ruby feed can mirror other gem servers that have a compact index. On the feed's settings page, add a mirror with the URL `https://rubygems.org` (see [Mirror](feeds.md#mirror-read-through-cache) for the settings every mirror has). Then:

- A gem's info file combines the versions pushed to the feed with those of the mirror. Gems that only the mirror has are listed too.
- `/versions` lists the gems of the feed and then all the gems of the mirror, because Bundler only looks up the gems listed there. That of rubygems.org has more than 20 MiB, which the feed keeps in memory for the [upstream listing cache](feeds.md#upstream-listing-cache) duration. Bundler downloads it once, and later only what was added at its end.
- A gem file is downloaded from the mirror when it is first asked for, checked against the mirror's SHA-256, stored in the feed and served from there afterwards. The gemspecs of gem files the feed doesn't have come from the mirror.
- A version pushed to the feed hides the same version of the mirror, and a version yanked in the feed is left out even when the mirror has it.
- With several mirrors, they are asked in order: the first one that has a gem lists all of its versions and serves its files. A mirror that is unreachable is skipped.

## In the web UI

A Ruby feed lists the gems stored in it, with search. A gem's page shows the readme (`README.md` of the gem), the Ruby versions it needs, its runtime dependencies, license and links, the versions, the gem file of each platform with its SHA-256 under **Files**, and the snippets for Bundler, the `Gemfile` and `gem install`. Gems that are only available from a mirror appear once a gem file has been downloaded.

Users with the **delete** permission can **yank** a version, **unyank** it, or delete it for good.

## What is different from NuGet feeds

| | Ruby feeds |
|---|---|
| Retention | Not available. Old versions are only removed by hand |
| Deletion behavior | Not available |
| Upload from the browser | Not available. Push with `gem push` |
| Full index (`specs.4.8.gz`) and dependency API | Not served. `gem search --remote` doesn't work |
| Yanking one platform of a version | Not available. A yank applies to all platforms of the version |

The NuGet, pub, npm, Maven, PyPI, Cargo and Go APIs answer `404 Not Found` on a Ruby feed, and the gem server answers `404 Not Found` on other feeds.
