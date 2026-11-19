# Visual Studio extensions

A feed of the type **VSIX** is a [private extension gallery](https://learn.microsoft.com/visualstudio/extensibility/private-galleries) for Visual Studio extensions: Visual Studio lists its extensions in **Manage Extensions**, installs them and offers their updates, VSIX files are uploaded to it with a plain HTTP request, and it can mirror other galleries.

A feed serves one kind of package. Its type is chosen when the feed is created and can't be changed afterwards. VS Code extensions go to a [VS Code feed](vscode.md).

## Create a VSIX feed

On **Admin > Feeds**, press **New feed** and choose the type **VSIX** (see [Managing feeds](feeds.md#managing-feeds)). The gallery is below `/vsix`, clear of the pages of the web UI. Visual Studio reads it from the **Gallery URL**, an Atom feed:

```
https://your-server/feeds/vs/vsix/atom.xml
```

With user accounts, Visual Studio uses another URL, with a token in it; see [Authentication](#authentication). The feed's **Connect** page shows the URL to use.

## Add the gallery to Visual Studio

In Visual Studio, open **Tools > Options > Environment > Extensions**, add an entry under **Additional Extension Galleries** with a name and the gallery URL, and apply. The gallery then appears under **Extensions > Manage Extensions**: Visual Studio lists its extensions with their names, descriptions, icons and preview images, installs them, and offers the updates of installed extensions that came from it.

A gallery lists one version of each extension, its newest listed one. Visual Studio installs that version, and offers it as an update when it is newer than the installed one.

To roll the gallery out to a team, it can also be added with a `.pkgdef` file instead of by hand (see [Private galleries](https://learn.microsoft.com/visualstudio/extensibility/private-galleries)). A downloaded VSIX file installs with `VSIXInstaller.exe`:

```
VSIXInstaller.exe /quiet MyExtension.vsix
```

### Authentication

Visual Studio can't sign in to an extension gallery: it only sends the Windows credentials of the user, never a username and password or a token. So with `Authentication:Mode` set to `Local`, `Entra` or `Hybrid`, the gallery has a second URL with a [personal access token](authentication.md#personal-access-tokens-pats) in its path:

```
https://your-server/feeds/vs/vsix/auth/<token>/atom.xml
```

Add this URL to Visual Studio instead. The links of the gallery carry the token too, so Visual Studio downloads the files and images the same way. The account of the token needs the **pull** permission on the feed (see [Feed permissions](authentication.md#feed-permissions)).

The token URL only reads: it serves the gallery, its files and images, and refuses uploads (`404 Not Found`), whatever the token's account may do otherwise. The token is still part of a URL, so it is stored in Visual Studio's settings and may appear in the logs of PaGetto (the request line that ASP.NET Core logs), proxies and load balancers. Use the token of an account that can only pull from the feed, give the token an expiry, and revoke it on **My Tokens** if it leaks.

With the `Legacy` mode, a server without configured credentials needs no token to read, and the gallery URL works as it is. A server with credentials (`Authentication:Credentials`) can't be read by Visual Studio.

## Upload

Visual Studio has no command to publish an extension to a private gallery. A VSIX file is uploaded to the feed with `PUT` (or `POST`) of the file as the body to `/vsix/upload`, which needs the **push** permission:

```bash
curl --fail-with-body -u __token__:<token> -T MyExtension.vsix \
  https://your-server/feeds/vs/vsix/upload
```

From PowerShell:

```powershell
$credentials = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("__token__:<token>"))
Invoke-RestMethod -Method Put -InFile .\MyExtension.vsix -Uri https://your-server/feeds/vs/vsix/upload `
  -Headers @{ Authorization = "Basic $credentials" }
```

With the `Legacy` mode, the password is one of the configured API keys and the username is not checked. PaGetto reads the extension's identifier and version from the file and checks each upload:

- The file must be a VSIX file as the VSIX project of the Visual Studio SDK builds it: a zip with `extension.vsixmanifest` and `[Content_Types].xml` at its root.
- The manifest must have the format of Visual Studio 2012 and later (`PackageManifest` version 2.0.0), a `DisplayName`, and at least one `InstallationTarget`. Manifests of Visual Studio 2010 (version 1.0.0) and VS Code extensions, which target `Microsoft.VisualStudio.Code` and go to a [VS Code feed](vscode.md), are refused.
- The `Id` of the manifest's `Identity` must have letters, digits, dots, dashes and underscores, start and end with a letter or digit, and have at most 128 characters. Identifiers are compared regardless of case, as Visual Studio does, and keep the case they were uploaded with.
- The `Version` must be two to four numbers separated by dots, such as `1.2.0`, as Visual Studio compares them. Leading zeros don't count, so `1.02` is `1.2`. `1.0` and `1.0.0` are different versions, the second being newer.
- The file must not be larger than the feed's **Max package size**.
- A version that the feed already has is rejected with `409 Conflict`, unless the feed's **Package overwrite policy** is **Allow all**. Extensions have no prerelease versions, so **Prerelease only** never replaces one.
- A read-only feed refuses every upload with `403 Forbidden`.

The feed answers `201 Created`, or an error with the reason as plain text, which `curl --fail-with-body` prints.

## The gallery

The feed answers these requests:

- `/vsix/atom.xml`: the gallery, an Atom feed with one entry for each extension: its newest listed version, with the extension's display name, description, publisher, the products it installs into (its installation targets), its download count (which can be up to five minutes old, as the gallery is cached) and links. Visual Studio compares the entry's `Vsix` element with the installed extensions to offer updates.
- `/vsix/extensions/{id}/{version}/{id}-{version}.vsix`: the VSIX file of a version. Every version is served at this path, whatever the file name.
- `/vsix/extensions/{id}/{version}/icon` and `/preview`: the icon and preview image of the manifest (PNG, JPEG, GIF, BMP or ICO), read from the VSIX file.
- `/vsix/auth/{token}/...`: the same, for Visual Studio with user accounts (see [Authentication](#authentication)).

An unknown extension or version is `404 Not Found`. An unlisted version is left out of the gallery, but it can still be downloaded. An extension without listed versions is left out.

## Mirror another gallery

A VSIX feed can mirror the Atom feeds of other private galleries, such as `https://www.vsixgallery.com/feed/`, a gallery made with [PrivateGalleryCreator](https://github.com/madskristensen/PrivateGalleryCreator), or the gallery of another VSIX feed. On the feed's settings page, add a mirror with the URL of the gallery's Atom feed (see [Mirror](feeds.md#mirror-read-through-cache) for the settings every mirror has). Then:

- The gallery combines the extensions uploaded to the feed with those of the mirror's feed. For each extension it offers the newest version: the feed's, or the mirror's when it is newer. A version of the feed hides the mirror's version of the same number.
- The mirror's extensions are linked on this feed, so Visual Studio needs no access to the mirror. A VSIX file is downloaded from the mirror when it is first asked for, stored in the feed and served from there afterwards. The images of extensions that are not stored yet are passed through.
- A gallery lists one version of each extension, so only that version can be mirrored. Galleries have no checksums, so the downloaded file is only checked to have the identifier and version of the mirror's entry.
- The mirror's credentials are sent to the mirror's host only, not to other hosts its entries link to.
- With several mirrors, they are asked in order: the first one whose feed has an extension offers it. A mirror that is unreachable is skipped.
- The feeds of mirrors are cached in memory, as on every feed type (see [Upstream listing cache](feeds.md#upstream-listing-cache)).

The Visual Studio Marketplace is not an Atom feed and can't be mirrored.

## In the web UI

A VSIX feed lists the extensions stored in it by their display names, with search. An extension's page shows its description, the Visual Studio versions it installs into, the extensions it depends on, its tags, identifier, publisher and links, the versions, a download link, the gallery URL and the `VSIXInstaller.exe` command. Extensions that are only available from a mirror appear once they have been downloaded.

Users with the **delete** permission can **unlist** a version, which leaves it out of the gallery while it can still be downloaded, **relist** it, or delete it for good.

## Feed settings and limits

Most [feed settings](feeds.md#feed-settings) apply to VSIX feeds as to any other feed. These differ:

| | VSIX feeds |
|---|---|
| Retention | The feed's own version limits, applied within an hour, and the removal of unused mirrored versions. See [auto-deletion](configuration.md#package-auto-deletion) |
| Deletion behavior | Not available |
| Upload from the browser | Not available. Upload with `curl` or another HTTP client |
| VS Code extensions | Hosted by [VS Code feeds](vscode.md) |
| Extension packs | Hosted, but the gallery doesn't list the extensions they contain |
| Ratings, reviews, the Marketplace's API | Not available |

The APIs of other feed types answer `404 Not Found` on a VSIX feed, and the gallery answers `404 Not Found` on feeds of other types.
