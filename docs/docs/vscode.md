# VS Code extensions

A feed of the type **VS Code** is an extension gallery for VS Code-based editors: it serves the gallery API that the editors query (the API of the Visual Studio Marketplace, which [Open VSX](https://open-vsx.org/) also serves), so the editor's **Extensions** view searches it, installs extensions from it and offers their updates. VSIX files are uploaded to it with a plain HTTP request, and it can mirror other galleries such as Open VSX.

A feed serves one kind of package. Its type is chosen when the feed is created and can't be changed afterwards. Visual Studio extensions go to a [VSIX feed](vsix.md).

## Which editors

An editor finds its extension gallery in the `extensionsGallery` section of its `product.json`. Editors built from VS Code's open source code let you change it:

| Editor | Where the gallery is set |
|---|---|
| [VSCodium](https://vscodium.com/) | A `product.json` in its user data folder (`%APPDATA%\VSCodium`, `~/Library/Application Support/VSCodium` or `~/.config/VSCodium`), or the `VSCODE_GALLERY_*` environment variables. See [its documentation](https://github.com/VSCodium/vscodium/blob/master/docs/extensions.md#howto-switch-marketplace) |
| [code-server](https://github.com/coder/code-server) | The `EXTENSIONS_GALLERY` environment variable, the same JSON as `extensionsGallery` |
| Code - OSS, and other editors built from it | The `product.json` of the build |

VS Code from Microsoft only uses the Visual Studio Marketplace, or a private marketplace that its enterprise policy (`extensions.gallery.serviceUrl`) signs in to with a GitHub or Microsoft account. It can't be pointed at a VS Code feed, but it installs a VSIX file downloaded from one:

```
code --install-extension hello-world-1.0.0.vsix
```

## Create a VS Code feed

On **Admin > Feeds**, press **New feed** and choose the type **VS Code** (see [Managing feeds](feeds.md#managing-feeds)). The gallery is below `/vscode`, clear of the pages of the web UI. The editor reads it from the **Gallery service URL**:

```
https://your-server/feeds/code/vscode/gallery
```

With user accounts, the editor uses another URL, with a token in it; see [Authentication](#authentication). The feed's **Connect** page shows the URLs to use.

## Point the editor at the gallery

Set the `extensionsGallery` of the editor's `product.json` to the feed's URLs:

```json
{
  "extensionsGallery": {
    "serviceUrl": "https://your-server/feeds/code/vscode/gallery",
    "itemUrl": "https://your-server/feeds/code/vscode/item",
    "extensionUrlTemplate": "https://your-server/feeds/code/vscode/gallery/vscode/{publisher}/{name}/latest"
  }
}
```

For code-server, the same JSON goes in an environment variable:

```bash
export EXTENSIONS_GALLERY='{"serviceUrl": "https://your-server/feeds/code/vscode/gallery", "itemUrl": "https://your-server/feeds/code/vscode/item", "extensionUrlTemplate": "https://your-server/feeds/code/vscode/gallery/vscode/{publisher}/{name}/latest"}'
code-server --install-extension contoso.hello-world
```

Restart the editor. The **Extensions** view then searches the feed, shows each extension's readme, changelog and icon, installs extensions (also with `--install-extension publisher.name` on the command line) and offers the updates of installed extensions. `itemUrl` opens the extension's page in the web UI.

Extensions uploaded to the feed have no signature, so the feed marks them as private extensions: the editor shows them as **Private Extension** and installs them, while it refuses unsigned public extensions from a gallery set in `product.json`. Extensions from a mirror keep the mirror's signature. As with any gallery, the editor asks whether you trust a publisher before it installs the first extension from it.

### Authentication

The editors can't sign in to an extension gallery: they send no credentials to the URLs of `product.json`. So with `Authentication:Mode` set to `Local`, `Entra` or `Hybrid`, the gallery has a second URL with a [personal access token](authentication.md#personal-access-tokens-pats) in its path:

```
https://your-server/feeds/code/vscode/auth/<token>/gallery
```

Use it as `serviceUrl` (and in `extensionUrlTemplate`). The URLs of the gallery's answers carry the token too, so the editor downloads the files the same way. The account of the token needs the **pull** permission on the feed (see [Feed permissions](authentication.md#feed-permissions)).

The token URL only reads and queries: it serves the gallery's queries and files, and refuses uploads (`404 Not Found`), whatever the token's account may do otherwise. The token is still part of a URL, so it is stored in `product.json` and may appear in the logs of PaGetto (the request line that ASP.NET Core logs), proxies and load balancers. Use the token of an account that can only pull from the feed, give the token an expiry, and revoke it on **My Tokens** if it leaks.

With the `Legacy` mode, a server without configured credentials needs no token to read, and the gallery URL works as it is. A server with credentials (`Authentication:Credentials`) can't be read by the editors.

## Upload

The gallery API has no command to publish an extension. Package the extension with [`vsce package`](https://code.visualstudio.com/api/working-with-extensions/publishing-extension#packaging-extensions), then upload the VSIX file to the feed with `PUT` (or `POST`) of the file as the body to `/vscode/upload`, which needs the **push** permission:

```bash
curl --fail-with-body -u __token__:<token> -T hello-world-1.0.0.vsix \
  https://your-server/feeds/code/vscode/upload
```

From PowerShell:

```powershell
$credentials = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("__token__:<token>"))
Invoke-RestMethod -Method Put -InFile .\hello-world-1.0.0.vsix -Uri https://your-server/feeds/code/vscode/upload `
  -Headers @{ Authorization = "Basic $credentials" }
```

With the `Legacy` mode, the password is one of the configured API keys and the username is not checked. PaGetto reads the extension's identifier, version and platform from the file and checks each upload:

- The file must be a VSIX file as `vsce package` builds it: a zip with `extension.vsixmanifest` at its root, whose installation target is `Microsoft.VisualStudio.Code`, and the extension's `package.json` as its `Microsoft.VisualStudio.Code.Manifest` asset. Visual Studio extensions are refused.
- The extension's identifier is `publisher.name`, from the `Publisher` and `Id` of the manifest's `Identity`. Each part must have letters, digits, dashes and underscores and start with a letter or digit, and the identifier must have at most 128 characters. Identifiers are compared regardless of case, as the editors do, and keep the case they were uploaded with.
- The `Version` must be a semantic version, such as `1.2.0`. A version is a pre-release when its manifest's `Microsoft.VisualStudio.Code.PreRelease` property is `true` (`vsce package --pre-release`) or it has a pre-release label.
- The `TargetPlatform` of a [platform-specific extension](https://code.visualstudio.com/api/working-with-extensions/publishing-extension#platformspecific-extensions) must be one the editors know, such as `linux-x64`, `win32-arm64`, `darwin-arm64`, `alpine-x64` or `web`. A version has one file for each platform: upload each platform's file (`vsce package --target linux-x64`) to the same version. A file without a platform is universal.
- The file must not be larger than the feed's **Max package size**.
- A version that the feed already has for that platform is rejected with `409 Conflict`, unless the feed's **Package overwrite policy** allows it (**Prerelease only** replaces pre-release versions).
- A read-only feed refuses every upload with `403 Forbidden`.

The feed answers `201 Created`, or an error with the reason as plain text, which `curl --fail-with-body` prints.

## The gallery

The feed answers these requests:

- `POST /vscode/gallery/extensionquery`: a query of the gallery API. A query for extensions by identifier answers them with their versions, one entry per version and platform, newest first; the editor picks the one that fits its platform and version. A query with search text, a category or a tag answers a page of the matching extensions: the feed's first, then the mirror's. The sort orders of the editor (installs, last update, title, publisher, publish date) apply to the feed's extensions.
- `GET /vscode/gallery/vscode/{publisher}/{name}/latest`: an extension with the latest stable and pre-release version of each platform, which the editors read before they install or update it.
- `GET /vscode/asset/{publisher}/{name}/{version}/{assetType}?targetPlatform={platform}`: a file of a version: the VSIX file (`Microsoft.VisualStudio.Services.VSIXPackage`, which counts a download), the manifest, and the assets the manifest lists, such as `package.json`, the readme, the changelog, the license and the icon.
- `GET /vscode/item?itemName={publisher}.{name}`: redirects to the extension's page in the web UI.
- `/vscode/auth/{token}/...`: the same, for the editors with user accounts (see [Authentication](#authentication)).

An unknown extension, version or platform is `404 Not Found`. An unlisted version is left out of the gallery, but its files can still be downloaded. An extension without listed versions is left out.

## Mirror another gallery

A VS Code feed can mirror other galleries of the gallery API, such as Open VSX (`https://open-vsx.org/vscode/gallery`) or the gallery of another VS Code feed. On the feed's settings page, add a mirror with the gallery's service URL, the `serviceUrl` of `product.json` (see [Mirror](feeds.md#mirror-read-through-cache) for the settings every mirror has). Then:

- An extension that has no version uploaded to the feed is looked up on the mirror, and offered with all the mirror's versions and platforms. An extension that has a version uploaded to the feed is never looked up on the mirror, so a mirror can't offer versions of the feed's own extensions.
- Searches list the feed's extensions first, then the mirror's, as one list with one total.
- The mirror's extensions are linked on this feed, so the editor needs no access to the mirror. A VSIX file is downloaded from the mirror when it is first asked for, stored in the feed and served from there afterwards; it is checked to have the identifier, version and platform the mirror lists. The other files of versions that are not stored yet (readme, changelog, icon) are passed through.
- The mirror's signatures are passed on: a stored file is the mirror's file as it is, so its signature stays valid, and the editor checks it as it would on the mirror.
- The mirror's credentials are sent to the mirror's host only, not to other hosts it links to.
- With several mirrors, they are asked in order: the first one that has an extension offers it, and searches go to the first one that answers. A mirror that is unreachable is skipped.
- The mirror's answers are cached in memory, as on every feed type (see [Upstream listing cache](feeds.md#upstream-listing-cache)).

The terms of use of the Visual Studio Marketplace limit its extensions to Microsoft's products, so mirror Open VSX instead.

## In the web UI

A VS Code feed lists the extensions stored in it by their display names, with search. An extension's page shows its readme, description, the VS Code versions it needs, the extensions it depends on, its tags, identifier, platforms and source repository, the versions, a download link and the `code --install-extension` commands. Extensions that are only available from a mirror appear once one of their files has been downloaded.

Users with the **delete** permission can **unlist** a version, which leaves it out of the gallery while its files can still be downloaded, **relist** it, or delete it for good with all its platforms.

## Feed settings and limits

Most [feed settings](feeds.md#feed-settings) apply to VS Code feeds as to any other feed. These differ:

| | VS Code feeds |
|---|---|
| Retention | The feed's own version limits, applied within an hour, and the removal of unused mirrored versions. See [auto-deletion](configuration.md#package-auto-deletion) |
| Deletion behavior | Not available |
| Upload from the browser | Not available. Upload with `curl` or another HTTP client |
| Signatures | Mirrored extensions keep the mirror's signature. Uploaded extensions are unsigned and marked as private extensions, which the editors install without a signature |
| Web extensions | The `web` platform is stored and served, but the gallery has no `resourceUrlTemplate`, so browser-based editors can't load web extensions from it |
| Ratings, reviews, statistics | Not available. Install counts are the feed's downloads, or the mirror's counts |
| VS Code from Microsoft | Can't be pointed at the feed; installs downloaded VSIX files |

The APIs of other feed types answer `404 Not Found` on a VS Code feed, and the gallery answers `404 Not Found` on feeds of other types.
