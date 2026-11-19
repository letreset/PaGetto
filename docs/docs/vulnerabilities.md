# Vulnerability scanning

PaGetto matches every stored package version, published to a feed or stored from a mirror, against the [OSV](https://osv.dev) vulnerability database. A version with a known advisory shows it on its package page with the version that fixes it, administrators see every vulnerable version on one page, and a new critical finding is sent to the feed's webhook.

## What is scanned

Every stored version of a feed whose ecosystem OSV knows:

| Feed type | OSV ecosystem | Package name sent |
|---|---|---|
| NuGet | `NuGet` | The package id |
| npm | `npm` | The package name |
| Maven | `Maven` | `groupId:artifactId` |
| Ivy | `Maven` | `organisation:module`, without the Scala and sbt versions |
| Python | `PyPI` | The normalized project name |
| Rust | `crates.io` | The crate name |
| Go | `Go` | The module path |
| Ruby | `RubyGems` | The gem name |
| Hex | `Hex` | The package name |
| PHP | `Packagist` | `vendor/package` |
| Dart | `Pub` | The package name |
| R | `CRAN` | The package name as published |

CocoaPods, Swift, VSIX and VS Code feeds are not scanned: OSV has no advisories for CocoaPods pods or Visual Studio and VS Code extensions, and it knows Swift packages by their repository URL, not by their registry identifier.

Versions that are only available from a mirror are scanned once they are stored in the feed, which happens on their first download.

## When

A background task looks for versions to scan every minute:

- A version that was never scanned, such as one that was just published or mirrored, is scanned within about a minute.
- Every version is scanned again `VulnerabilityScanning:RescanIntervalHours` hours (default 24) after its last scan, so advisories published after it was stored are found.

Each scan replaces the version's findings with what the database says now: new advisories are added, changed ones are updated, and advisories that were withdrawn or no longer affect the version are removed.

When the database can't be reached, the versions stay due, a warning is logged, and the task tries again after 15 minutes. Nothing else on the server depends on it: packages are published, mirrored and downloaded as before.

## Severity

An advisory can have several severities. PaGetto uses:

1. The CVSS v3 base score, computed from the advisory's CVSS v3 vector (the highest one when there are several), with the CVSS ratings: 9.0 and above is **Critical**, 7.0 to 8.9 **High**, 4.0 to 6.9 **Moderate**, below 4.0 **Low**. The score means the same in every ecosystem, so findings can be compared across feeds.
2. Without a CVSS v3 vector (for example an advisory with only a CVSS v4 vector), the database's own rating, which GitHub advisories carry (`LOW`, `MODERATE`, `HIGH`, `CRITICAL`).
3. Otherwise **Unknown**.

## Where findings appear

- **Package page**: a version with known vulnerabilities has a card above the install commands that lists each advisory with its severity, its CVSS score, its aliases (such as the CVE id), a link to it on osv.dev, the affected versions and the lowest version above the shown one that fixes it.
- **Admin > Vulnerabilities**: every finding across feeds, most severe first, filtered by feed, severity (a severity and everything above it) and package name or advisory id. **Export CSV** and **Export JSON** download every finding that matches the filters. See [Web UI](web-ui.md#vulnerabilities).
- **Webhook**: a new **Critical** finding is sent to the feed's [webhook](feeds.md#webhook) as the event `vulnerability_found`, with the advisory id in `advisory`.
- **npm audit**: on npm feeds, `npm audit` is answered from the findings of the versions stored in the feed. See [npm audit](npm.md#npm-audit).

## Configuration

```json
{
    ...

    "VulnerabilityScanning": {
        "Enabled": true,
        "ApiUrl": "https://api.osv.dev/",
        "RescanIntervalHours": 24,
        "TimeoutSeconds": 30
    },

    ...
}
```

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Whether stored versions are scanned. Turn it off for servers without access to the database. Findings stored before stay on the pages |
| `ApiUrl` | `https://api.osv.dev/` | The OSV API to ask, for example a self-hosted mirror. It must serve `POST /v1/querybatch` and `GET /v1/vulns/{id}` |
| `RescanIntervalHours` | `24` | How many hours after its last scan a version is scanned again, from 1 to 8760 |
| `TimeoutSeconds` | `30` | How long one request to the API may take, from 1 to 600 |

### What is sent

The scan sends the ecosystem, the name and the version of every stored version to `ApiUrl`, including those of packages published only to your feeds. If the names of your own packages must not leave your network, point `ApiUrl` at a self-hosted OSV mirror, or turn scanning off.

### Servers without internet access

Set `VulnerabilityScanning:Enabled` to `false`, or point `ApiUrl` at an OSV mirror inside your network. With scanning off, npm feeds forward `npm audit` to their mirrors as before.

### Several replicas

Each replica runs the background task. Scans of the same version at the same moment are harmless: the database keeps one finding per version and advisory, and a replica whose write collides tries again on its next run. A new critical finding can then be sent to the webhook more than once.
