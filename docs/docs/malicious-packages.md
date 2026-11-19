# Malicious packages

Typosquatting and deliberately malicious packages show up in public registries every day. A feed that mirrors a public registry would pass them on to your developers and store them. PaGetto keeps a local copy of the [OpenSSF malicious packages](https://github.com/ossf/malicious-packages) data and refuses to mirror a version on it, or to serve a mirrored copy of one.

## What is blocked

A version is blocked when all of these are true:

- The feed mirrors (it has at least one enabled mirror) and blocks malicious packages: its **Block malicious packages** setting, or `MaliciousPackages:Enabled` when the feed uses the global default.
- The malicious packages data lists the version: an advisory with an id that starts with `MAL-` names the package, and every version of it or this version.
- The version would be stored from a mirror, or it was stored from a mirror before it was listed.
- No administrator [allowed](#allowing-a-package) the package or the version.

Versions published to a feed are never blocked. Dependency confusion attacks publish public packages under the names of private ones, so the data often lists names that are also the names of your own packages.

The data covers the ecosystems of these feed types, with package names matched as for [vulnerability scanning](vulnerabilities.md#what-is-scanned): NuGet, npm, Maven, Ivy, Python, Rust, Go, Ruby, Hex, PHP, Dart and R. It has no data for CocoaPods, Swift or VSIX feeds, so they are not checked.

## What clients see

A blocked download is answered with `403 Forbidden` and a message such as:

```text
evil-pkg 1.0.0 is blocked: it is listed as a malicious package (MAL-2025-1234). An administrator can allow it if this is a false positive.
```

The message is in the format the client shows: the `error` of a JSON body for npm, the error object of the pub API for Dart, the `errors` list of the registry API for Cargo, and otherwise the reason phrase and a plain text body (NuGet, Maven, Ivy and Python clients show the reason phrase; the go command, Bundler, Composer, R and Mix show the body). The package's version listings still name the version, so the client fails with this message instead of picking another version silently.

Every blocked download is:

- logged as the audit event `malicious_package_blocked`, with the feed, package, version and advisory (`advisory=MAL-…` in the detail), and listed on **Admin > Audit log**;
- sent to the feed's [webhook](feeds.md#webhook) as the event `malicious_package_blocked`, with the advisory id in `advisory`.

## The local copy

A background task keeps one copy per ecosystem that a mirroring, blocking feed needs. It checks every minute, and refreshes an ecosystem `MaliciousPackages:RefreshIntervalHours` hours (default 1) after its last refresh:

- The first copy is read from the ecosystem's `all.zip` of the [OSV data export](https://google.github.io/osv.dev/data/#data-dumps). For npm this is a download of over 200 MB, once.
- Later refreshes read the ecosystem's `modified_id.csv` until the last change they already have, and download only the advisories changed since. When more than 2,000 changed, the whole archive is read again.
- Withdrawn advisories (false positives the data removes) stop blocking at the next refresh.

When a refresh fails, the copy stays as it was and downloads are still checked against it. A warning is logged, **Admin > Malicious packages** shows the error, and the refresh is tried again after 15 minutes.

Downloads are checked against the database, not against the export, so they never wait for it.

## Allowing a package

On **Admin > Malicious packages** (see [Web UI](web-ui.md#malicious-packages)), an administrator can:

- look up a package to see the advisories that list it;
- allow a package, or one version of it, with a reason. The allowance applies to every feed of the ecosystem;
- remove an allowance, which blocks the package again.

Adding and removing an allowance are recorded in the audit log as `malicious_package_allowed` and `malicious_package_allowance_removed`, with the package as the target (`npm:evil-pkg@1.0.0`) and the reason in the detail.

## Configuration

```json
{
    ...

    "MaliciousPackages": {
        "Enabled": true,
        "DataUrl": "https://osv-vulnerabilities.storage.googleapis.com/",
        "RefreshIntervalHours": 1,
        "TimeoutSeconds": 900
    },

    ...
}
```

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Whether feeds block malicious packages, unless a feed's own setting says otherwise |
| `DataUrl` | `https://osv-vulnerabilities.storage.googleapis.com/` | The OSV data export: a folder per ecosystem with `all.zip`, `modified_id.csv` and an `{id}.json` per advisory. A mirror of it inside your network works too |
| `RefreshIntervalHours` | `1` | How many hours after its last refresh an ecosystem is refreshed again, from 1 to 168 |
| `TimeoutSeconds` | `900` | How long one download from `DataUrl` may take, from 1 to 7200 |

Each feed's **Block malicious packages** setting (in the **Mirrors** section of its settings) overrides `Enabled` for that feed.

### What is sent

Nothing about your packages: the refresh only downloads the export. Downloads are checked locally.

### Servers without internet access

Set `MaliciousPackages:Enabled` to `false`, or point `DataUrl` at a copy of the export inside your network. A copy only needs the files of the ecosystems your feeds mirror.

### Several replicas

The copy is in the database, so every replica checks against the same list. Before a replica refreshes an ecosystem it marks the attempt, and the other replicas leave that ecosystem alone for 15 minutes.
