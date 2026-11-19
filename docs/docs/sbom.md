# SBOM

PaGetto makes a software bill of materials (SBOM) for every stored package version, in [CycloneDX](https://cyclonedx.org) 1.6 JSON. It describes the version with what PaGetto already stored with it: its name, version, license, checksums and the dependencies its manifest declares, plus its known [vulnerabilities](vulnerabilities.md). Every feed type has one.

## Download

- **Web UI**: **Download SBOM** in the sidebar of a package page saves the SBOM of the shown version as `{name}-{version}.cdx.json`.
- **API**: `GET {feed}/api/v1/sbom/{id}/{version}` answers `application/vnd.cyclonedx+json; version=1.6`. A name with a slash (a scoped npm package, a Go module or a PHP package) has it encoded as `%2F`:

```bash
curl -u alice:<token> http://localhost:5000/api/v1/sbom/Newtonsoft.Json/13.0.3
curl -u alice:<token> http://localhost:5000/feeds/js/api/v1/sbom/%40acme%2Fui-kit/1.0.0
```

The request needs the same read access as a download: the pull permission on the feed with user accounts, and in `Legacy` mode the configured credentials when the server has any. Unlisted versions have an SBOM too; a quarantined version and a version that is only available from a mirror answer 404 until they are stored.

## What it contains

| CycloneDX field | Content |
|---|---|
| `metadata.component` | The version: its name as published, version, description, [package URL](https://github.com/package-url/purl-spec) and license. A Maven or Ivy group, an npm scope and a PHP vendor are its `group`. |
| `metadata.component.hashes` | The SHA-256 of the version's archive. NuGet packages have none. |
| `metadata.component.components` | For a version made of several files (Maven, Ivy, Python, Ruby, VS Code), each file with its MD5, SHA-1, SHA-256 and SHA-512. |
| `components` | Each package the manifest declares as a dependency, with its package URL and the requirement it is declared with in the property `pagetto:requirement` (e.g. `^1.3.0`, or `[13.0.1, ) (net8.0)` for a NuGet dependency of one target framework). A package declared more than once has one component with a property per declaration. |
| `dependencies` | The version depends on each of those components. |
| `vulnerabilities` | The known vulnerabilities of the version from the [vulnerability scan](vulnerabilities.md): the advisory id and its aliases linked to osv.dev, the severity and CVSS v3 score, the summary, and the version that fixes it as the recommendation. |

A license that reads as an SPDX expression (`MIT`, `MIT OR Apache-2.0`) is written as an expression; anything else, such as `Apache License, Version 2.0` from a POM, as a license name.

### Declared dependencies only

The dependencies are the ones the version's manifest declares, as its package page shows them. They are not resolved: a component names a package without a version, and the requirement says which versions are accepted. An SBOM of what a build actually used comes from the build tool (for example `dotnet CycloneDX`, `cyclonedx-npm` or `cyclonedx-gomod`).

What each ecosystem declares:

| Feed type | Dependencies |
|---|---|
| NuGet | The dependencies of each target framework |
| npm | `dependencies` |
| Maven | The POM's dependencies, with their scope (a version from a parent or BOM is `managed`) |
| Ivy | The ivy.xml dependencies, with their configuration mapping |
| Python | `Requires-Dist`, with extras and markers |
| Rust | The dependencies other than development dependencies |
| Go | Every `require` of `go.mod`, indirect ones marked `(indirect)` |
| Ruby | The runtime dependencies |
| Hex | The requirements, optional ones marked `(optional)` |
| PHP | `require`, without PHP and its extensions |
| Dart | `dependencies` |
| CocoaPods | The podspec's dependencies |
| R | `Depends`, `Imports` and `LinkingTo`, without R |
| VSIX | The extension's dependencies |
| VS Code | `extensionDependencies` and the extensions of an extension pack |
| Swift | None: PaGetto doesn't read `Package.swift` |

### Package URLs

Each component has the package URL of its ecosystem: `nuget`, `npm`, `maven` (Maven and Ivy), `pypi`, `cargo`, `golang`, `gem`, `hex`, `composer`, `pub`, `cocoapods`, `cran` and `swift`. A Swift package's namespace is its registry scope (`pkg:swift/mona/LinkedList@1.1.1`). The package URL specification has no type for Visual Studio and VS Code extensions, so their components have none and are referred to by name.
