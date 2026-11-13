# UI test checklist

Run these checks against the [test environment](README.md) after UI changes and before a release, so a broken page is noticed early. Start from a fresh environment:

```bash
docker compose -f testenv/docker-compose.yml down -v
docker compose -f testenv/docker-compose.yml up -d
```

Each check names the account to use (passwords are in [README.md](README.md)) and the expected result. Checks marked **Known issue** currently fail because of the linked issue; they should pass once it is fixed. Agents run these checks in the browser through the chrome-devtools MCP server, with one isolated browser context per account.

For every page you open, the browser console should show no errors.

## Sign-in

| # | Account | Steps | Expected |
|---|---|---|---|
| S1 | none | Open `/` | "Sign in required" and a **Sign in** button, no packages or feed names, and the page title is "Sign in required - PaGetto" |
| S2 | none | Sign in as `admin` with a wrong password | "Invalid username or password.", the username is kept |
| S3 | none | Sign in as `admin` | Lands on the first feed in the Admin > Feeds order, user menu shows `admin` |
| S4 | none | Open `/Login?ReturnUrl=https%3A%2F%2Fexample.com%2F` and sign in | Stays on the PaGetto site |
| S5 | none | Sign in as `build-agent` | Refused: this account can't sign in to the web UI |
| S6 | `admin` | User menu > **Sign out** | Back to the sign-in prompt |
| S6a | `admin` | Open Internal (`/feeds/internal/`), then user menu > **Sign out** | The form posts to `/Logout`, the session ends and the browser lands on `/` with the sign-in prompt |
| S7 | none | Sign in as `bob` with a wrong password 5 times, then with the right one | "This account is locked due to too many failed attempts" (resets after 15 minutes or with `down -v`) |
| S8 | `alice` | User menu > **Change password**, change it with the wrong current password, then with the right one | Wrong one: "The current password is incorrect."; right one: "Your password has been changed." and signing in again needs the new password |
| S9 | none | Start with `TESTENV_EMPTY=1` and sign in as `admin` / `admin` | Lands on **Change password** with a warning; opening any other page returns there; after changing the password the site works normally |

## Navigation

| # | Account | Steps | Expected |
|---|---|---|---|
| N1 | `admin` | Open the feed switcher (the pill next to the logo) | Default, Internal, Experimental, Archive, Dart in that order, each with its type badge (NuGet, and Dart on the last), its description and a check on the current feed |
| N2 | `carol` | Look at the header links on Internal | Packages, Connect, Statistics, Docs. No Upload (no push permission), and no **Upload package** button on the package list |
| N3 | `carol` | Look at the header links on Experimental | Packages, Connect, Upload, Statistics, Docs |
| N4 | `carol` | Open `/Admin/Feeds` | Redirected to the package list |
| N5 | `admin` | Open **Statistics** on Internal | Cards with 22 packages ("1 unlisted version"), 28 versions ("28 stable · 0 prerelease"), 171 total downloads and the stored package size; the server version and the services in use below them. **Most downloaded** starts with Contoso.Core (50), Contoso.Logging (47), Contoso.Testing (22); **Recently published** lists 10 versions, newest first, without Contoso.Logging 1.4.0 |
| N6 | `admin` | Open `/feeds/nope/` | 404 |
| N7 | `admin` | Press the moon icon in the header, reload, then press the sun icon | The whole UI switches to dark mode and stays dark after the reload; the header keeps its orange bottom line. Without a saved choice the system setting decides |

## Package list, search and filters

| # | Account | Steps | Expected |
|---|---|---|---|
| L1 | `admin` | Open Internal | "22 packages" next to the title, 20 packages and a page link **2**; page 2 lists the remaining 2 |
| L2 | `admin` | Search `logging` | Contoso.Logging |
| L3 | `admin` | Search `LOGGING` | Same result (case-insensitive) |
| L4 | `admin` | Search `observability` | Contoso.Logging and Contoso.Metrics (tag and description matches) |
| L5 | `admin` | Tag filter: type `obs` in the dropdown and pick `observability` | Contoso.Logging and Contoso.Metrics |
| L6 | `admin` | Framework filter: `.NET Standard 2.0` | All Contoso packages |
| L7 | `admin` | Framework dropdown on Internal | `.NET 10.0`, `.NET 8.0`, `.NET Standard 2.0`, in that order. On Default (with mirrored packages) every entry has a readable name, grouped by family, newest first; after R3 the portable profiles read e.g. `.NET Portable (net45, win8, wp8, wpa81)` |
| L8 | `admin` | Experimental, switch off **Prerelease** | "No packages match these filters." (all of them are prereleases) |
| L9 | `admin` | Open `/feeds/internal?p=99` | "No packages found" with a link to the first page |
| L10 | `admin` | Internal, switch to the grid view (next to the filters), reload | Package cards in a grid, still after the reload; the list button switches back |
| L11 | `admin` | Internal, press `/`, then an **Install** button | The cursor jumps to the search field; the button briefly reads "Copied" and the clipboard holds `dotnet add package … --version …` |
| L12 | `admin` | Open Default (empty) | "Nothing in Default yet" with **Upload a package** and **Connect a client** |

## Package page

| # | Account | Steps | Expected |
|---|---|---|---|
| P1 | `admin` | Open Internal > Contoso.Logging | Version 2.0.0 with a **latest** label, install tabs (.NET CLI, Package Manager, PackageReference, CPM, Paket, Script, File-based apps, Cake) on one line on a desktop screen, and **Copy** turns into "Copied" for a moment |
| P2 | `admin` | **Readme** tab (open by default) | The Contoso.Logging readme with a code block |
| P3 | `admin` | **Dependencies** tab | Contoso.Core (>= 2.0.0), grouped by target framework; the tab shows the number of dependencies |
| P4 | `admin` | Open Contoso.Core, **Used by** tab | Contoso.Logging (47 downloads, once, although two of its versions depend on Contoso.Core) and Contoso.Configuration (15) |
| P5 | `admin` | **Versions** tab of Contoso.Logging | 2.0.0 (highlighted as **current**), 1.5.0, and 1.4.0 struck through with an **unlisted** label and **Relist**; a download bar per stored version |
| P6 | `carol` | Versions of Contoso.Logging | 2.0.0 and 1.5.0 only, no Manage section |
| P7 | `admin` | Open `/feeds/internal/packages/Contoso.Logging/99.0.0` | "Version not found" with a link to the latest version (also for `/not-a-version`) |
| P8 | `admin` | **Download .nupkg** in the sidebar | Downloads `contoso.logging.2.0.0.nupkg` |
| P9 | `admin` | Look under the Contoso.Logging title, hover a badge | Badges `.NET 8.0` and `.NET Standard 2.0`; the tooltip says the package is compatible with that framework or higher |
| P10 | `admin` | **Frameworks** tab on Contoso.Logging | `.NET 10.0`, `.NET 8.0`, `.NET Standard 2.0`, in that order |
| P11 | `admin` | Contoso.Logging > **CPM** and **Cake** tabs, copy each | One line per entry (`PackageVersion` and `PackageReference`; `#addin` and `#tool`), and the copied text has the same lines. **Package Manager** shows `NuGet\Install-Package Contoso.Logging -Version 2.0.0` |
| P12 | `admin` | Experimental > Contoso.Preview.Ai > **Cake** tab | Both lines end with `&prerelease` (no `&amp;`) |
| P13 | `admin` | Experimental > Contoso.Preview.Ai, then Internal > Contoso.Logging | "This is a prerelease version of Contoso.Preview.Ai." under the install box; no such note on Contoso.Logging |
| P14 | `admin` | Statistics card of Contoso.Logging | 47 Total, 35 This version, and a Per day value |
| P15 | `admin` | Default > `/packages/Newtonsoft.Json` (needs internet), **Versions** tab, switch off **Include prerelease** | Only the prerelease rows disappear, the remaining stable versions are all shown (no 5-row cap) and **Show all … versions** is hidden; switching it on again restores the list. Contoso.Logging (no prereleases) has no switch |
| P16 | `admin` | **About** card of Internal > Contoso.Logging | The license link reads "MIT license" and opens `https://licenses.nuget.org/MIT`; **Size** shows the package size, e.g. "8.21 KB" |
| P17 | `carol` | Pack a package with `<Copyright>Copyright (c) Contoso</Copyright>` and push it to Experimental, then open it | The copyright text in the **About** card |
| P18 | `admin` | Contoso.Logging > **Atom feed** in the sidebar; then `curl -u carol:<password> http://localhost:5000/feeds/internal/packages/Contoso.Logging/atom.xml` | The link points to `/feeds/internal/packages/contoso.logging/atom.xml` and the page head has a `<link rel="alternate" type="application/atom+xml">`; curl returns an Atom feed with 2.0.0 and 1.5.0 (not the unlisted 1.4.0), and without `-u` it gets 401 |

## Package management

| # | Account | Steps | Expected |
|---|---|---|---|
| M1 | `admin` | Contoso.Logging 1.4.0 > **Relist** | 1.4.0 is listed again; **Unlist** it again afterwards |
| M2 | `alice` | Internal > Contoso.Testing > **Unlist**, then **Relist** | Both work (Package owners have delete on Internal) |
| M3 | `carol` | Experimental > Contoso.Preview.Ai 0.1.0-alpha.1 > **Delete** | Confirmation, then the version is gone |
| M4 | `admin` | Archive (read-only) > Contoso.Legacy > **Unlist** | No Manage section and no Relist links; a crafted Unlist POST returns 403 and the version stays listed |
| M5 | `admin` | Do M1, then check the container log (`docker compose -f testenv/docker-compose.yml logs pagetto`) | An `AUDIT package_relist_succeeded` line (and `package_unlist_succeeded` for the unlist), with `actor=admin` |
| M6 | `admin` | Disable and enable `bob` on Admin > Accounts, then check the container log | `AUDIT account_disabled target=bob` and `AUDIT account_enabled target=bob` lines with `actor=admin` |

## Connect and Upload

| # | Account | Steps | Expected |
|---|---|---|---|
| C1 | `carol` | Internal > **Connect** | Service index `http://localhost:5000/feeds/internal/v3/index.json`, copy button, tabs for .NET CLI, NuGet, nuget.config, Paket |
| C2 | `carol` | Read the authentication text, then open **My Tokens** in the user menu | The text points to My Tokens; the page opens, and a new token is shown once and can be revoked |
| C2a | `admin` | Admin > Accounts > `build-agent` > ⋯ > **New token…** | After **Create token**, a dialog shows the token once with a **Copy** button |
| C3 | `carol` | Experimental > **Upload** | An **Upload from browser** card ("Up to 8 GB per file"), then push commands for the Experimental service index |
| C3a | `carol` | Experimental > Upload, pick `tests/PaGetto.Tests/TestData/TestData.1.2.3.nupkg` and `.snupkg`, check the preview, then **Upload** | Preview shows TestData 1.2.3 (the `.snupkg` with a **Symbols** pill), authors, description and `net5.0` dependencies; after Upload both show "Published." with a **View package** link, and the log has `AUDIT package_upload_succeeded` and `symbol_upload_succeeded` lines with `actor=carol` |
| C3b | `carol` | Experimental > Upload, pick `testenv/packages/Contoso.Preview.Ai.0.1.0-alpha.1.nupkg` | "This version already exists in the feed." before anything is sent; with no other file, **Upload** stays disabled |
| C3c | `carol` | Experimental > Upload, pick `testenv/packages/Contoso.Logging.2.0.0.snupkg`, then **Upload** | The preview warns that the package isn't in the feed yet; after Upload: "Upload the package before its symbols." |
| C3d | `admin` | Archive > **Upload** | A "Read-only feed" note instead of the upload card; the push commands are still shown |
| C3e | `carol` | Experimental > Upload, pick a file that isn't a package (e.g. `testenv/README.md`) | "Only .nupkg and .snupkg files can be uploaded." |
| C4 | `carol` | Open `/feeds/internal/Upload` directly (pull only on Internal) | 404; the feed name and service index are not shown |
| C5 | `bob` | After `admin` clears Pull on Internal for Developers (as in G4), open `/feeds/internal/Upload` | 404; restore the permission afterwards |

## Admin > Feeds

| # | Account | Steps | Expected |
|---|---|---|---|
| F1 | `admin` | Type `Bad Slug!` into the slug field of **New feed**; then post the create form with the slug `Bad Slug` directly (e.g. from the browser console) | The field lowercases the text and drops the invalid characters while you type (`badslug`); the direct post gets "Slug must be lowercase alphanumeric with optional hyphens…" and nothing is created |
| F2 | `admin` | Create a feed with slug `internal` | "already exists" |
| F3 | `admin` | Create `ui-test`, drag it above Internal, reload | The new order is kept, also in the feed switcher |
| F4 | `admin` | Change the display name of `ui-test` in its **Settings**, then delete it | Both work; the feed disappears from the switcher |

## Admin > Feed settings

| # | Account | Steps | Expected |
|---|---|---|---|
| FS1 | `admin` | Open Internal > Settings | Overwrite policy **Prerelease only** and Max major versions **3**, with their "Use global default" boxes cleared |
| FS2 | `admin` | Change a field and press **Save settings** (**Save** on a small screen), then reopen | No save bar until the first change; then it slides in with "You have unsaved changes", on one line on a small screen. After saving, the toast says the settings were saved, the bar is gone and the values match FS1 apart from the change |
| FS2a | `admin` | Tick **Use global default** for Max major versions, clear it again | While ticked the field is disabled and shows the global value; cleared, it shows `3` again |
| FS3 | `admin` | Enter `-3` for Max major versions and save | A message next to the field, "nothing was saved", and the stored value is unchanged |
| FS4 | `admin` | Default > Settings > Mirrors | One mirror, `https://api.nuget.org/v3/index.json`, enabled |
| FS5 | `admin` | Add a mirror with the URL `not a url` and save | "the package source must be an absolute http(s) URL" |
| FS6 | `admin` | Add two mirrors, move the second up with the arrow, remove one | Titles renumber (Mirror 1, Mirror 2); do `down -v` afterwards |
| FS7 | `admin` | Experimental > Settings: clear **Use global default** for Read-only mode, turn **Read-only** on, save and reload; then tick **Use global default** again and save | The switch toggles and stays on after the reload, and a push to Experimental gets 403; with the global default the feed is writable again |
| FS8 | `admin` | Experimental > Settings > Retention: clear **Use global default** for Prereleases of older majors, turn **Delete** on, save and reload; then tick **Use global default** again and save | The switch is disabled and shows the global value (off) while the box is ticked; once cleared it toggles and stays on after the reload; with the global default it is off again |

## Admin > Accounts

| # | Account | Steps | Expected |
|---|---|---|---|
| A1 | `admin` | Create `dave` with the password `short` | "Password must be at least 12 characters." |
| A2 | `admin` | Create `ALICE` with a valid password | "already exists", and signing in as `Alice` works like `alice` |
| A3 | `admin` | Disable `bob`, then check a signed-in `bob` session | `bob` is signed out on the next request; enable again |
| A4 | `admin` | Disable `dave` and delete him (⋯ > **Delete account…**, only offered for disabled accounts) | The dialog shows the username; the account is gone |
| A5 | `admin` | Look at the `admin` row and its ⋯ menu | An **Admin** label; the menu only has **Reset password…** and **New token…** on the signed-in admin's own row |
| A5a | `admin` | `alice` > ⋯ > **Make admin**, sign in as `alice` and open Admin > Accounts; then **Remove admin role** as `admin` | `alice` can open the page while she is an admin |
| A5b | `admin` | After S7 (`bob` locked), open Admin > Accounts and **Unlock** `bob` | A "Locked until" label before; `bob` can sign in right away after |
| A5c | `admin` | `carol` > ⋯ > **Edit account…**, rename her to `caroline` with a new display name, then sign in as `caroline` | The dialog opens filled with her current values; the list shows the new name, and `caroline` signs in with her old password while `carol` no longer works |
| A6 | `admin` | `bob` > ⋯ > **Reset password…** with a 12+ character password (**Set password** stays disabled below 12), then sign in as `bob` with it | "Password of 'bob' reset successfully.", and the sign-in works (also right after S7's lockout) |
| A7 | `admin` | Look at the account list | Column headers on a desktop screen; each row has the avatar, groups, an Enabled/Disabled and a "Web + API"/"API only" label, the date and a ⋯ button whose menu isn't cut off, also on the last row |
| A8 | `admin` | Open ⋯ > **New token…** on `build-agent` (the last row), press Esc, open it again, type a name and press Enter | A dialog with the cursor in the name field; Esc closes it; Enter creates the token |
| A9 | `admin` | Create two tokens for `build-agent` with **New token…**, push a package with one of them, then open ⋯ > **Tokens** on `build-agent` and **Revoke** that token | The list shows each token's name, prefix, created, last used (a date for the used one) and expiry, never the token itself. After revoking: the toast "Token '…' revoked.", the row shows Revoked, the next push with that token gets 401, and **Audit log** has `token_revoked` for `build-agent` |
| A10 | `admin` | Do A9, then **Revoke all** on `build-agent`'s tokens | The confirmation names the number of active tokens; afterwards every token is revoked or expired, **Revoke all** is gone and **Audit log** has `tokens_revoked_all` with the count. As `alice`, `/admin/usertokens?userId=…` redirects to the package list |

## Admin > Groups & Permissions

| # | Account | Steps | Expected |
|---|---|---|---|
| G1 | `admin` | Open Developers > **Feed permissions** | "5 of 5 feeds"; Pull on Default, Internal, Archive, Dart; Pull, Push, Delete on Experimental |
| G2 | `admin` | Create a group `Developers`, then `developers` | "already exists" both times |
| G3 | `admin` | Remove `carol` from Developers (× on her chip), then check `carol` | `carol` sees "No feeds available"; add her back with **Add member** (the list only offers accounts that aren't members yet) and she sees all five feeds again |
| G4 | `admin` | Open Developers > **Feed permissions**, clear Pull on Internal and **Save permissions** | The button is disabled until a box changes, then shows "Unsaved changes" next to it (ticking the box again disables it again). After saving, `bob` no longer sees Internal; restore it |
| G5 | `admin` | Do G4, then check the container log | One `AUDIT feed_permission_revoked` line for Internal and one `feed_permission_set` line for the restore, no lines for the unchanged feeds |
| G6 | `admin` | Create a group, open its delete confirmation in two tabs, confirm in both | The first shows the toast "Group deleted successfully.", the second "Group not found." |
| G7 | `admin` | Create a group `Temp`, add `carol` and give it Pull on Internal. Click its pencil, rename it to `Developers`; then rename it to `Temporary` with a new description and **Save** | The first shows "Group 'Developers' already exists." with the form still open and nothing changed; the second shows the toast "Group 'Temporary' updated successfully.", the new name and description, `carol` still a member and Pull on Internal kept. The container log has `AUDIT group_updated` for `Temp` with `name=Temporary`. Delete the group afterwards |

## Admin > Audit log

| # | Account | Steps | Expected |
|---|---|---|---|
| U1 | `admin` | Create a group `Audit test`, delete it, then open **Audit log** | The newest rows are `group_deleted` and `group_created` for `Audit test`, with actor `admin`, the time in UTC and your IP address |
| U2 | `alice` | Push a package from `testenv/packages` to Internal, then push the same version again (409); as `admin`, open **Audit log** with Feed = Internal | The newest row is `package_upload_succeeded` with the package id and version, actor `alice`, above the uploads `admin` made when the data set was built; no row for the rejected push |
| U3 | `admin` | Filter by Event = `group_created` and a From date after today, then **Clear** | "No audit events found." while filtered; all events again after **Clear** |
| U4 | `alice` | Open `/admin/audit` | Redirected to the package list |

## Access control

| # | Account | Steps | Expected |
|---|---|---|---|
| X1 | none | Open `/feeds/internal/packages/Contoso.Logging` | Sign-in prompt, and the page title doesn't name the package |
| X2 | `carol` | Open `/feeds/internal/packages/Contoso.Logging` | No Manage section; a crafted Unlist POST returns 403 |
| X1a | none | Open `/feeds/internal/` and `/feeds/does-not-exist/`; then `curl -i http://localhost:5000/feeds/internal/v3/search` and the same for `does-not-exist` | Both pages show the same sign-in prompt; both `curl` calls get 401 with `WWW-Authenticate: Basic realm="NuGet Server"` |
| X2a | `build-agent` | `curl -i -u build-agent:<password> http://localhost:5000/feeds/archive/v3/search` (no pull permission there), and the same for `/feeds/does-not-exist/v3/search` | 404 for both, without a `WWW-Authenticate` header |
| X3 | none | `curl -u carol:<password> -X PUT -F package=@testenv/packages/Contoso.Mail.1.0.0.nupkg http://localhost:5000/feeds/internal/api/v2/package` | 403 (the same request without `-u` gets 401) |
| X3a | `build-agent` | `curl -i -u build-agent:<password> -X PUT -F package=@testenv/packages/Contoso.Mail.1.0.0.nupkg http://localhost:5000/feeds/archive/api/v2/package` (no permission there), and the same for `/feeds/does-not-exist/api/v2/package` | 404 for both |

## Mirror

| # | Account | Steps | Expected |
|---|---|---|---|
| R1 | `admin` | Open Default > `/packages/Newtonsoft.Json` (needs internet) | The package page with nuget.org's versions |
| R2 | `admin` | Look at the versions | Versions not stored in the feed carry a **mirror** label, no downloads, no `1900-01-01` dates and no Relist; upstream-unlisted versions aren't listed; opening a mirror-only version shows no Manage section |
| R3 | `admin` | Download a mirror-only version, e.g. `curl -u admin:<password> -o NUL http://localhost:5000/v3/package/newtonsoft.json/12.0.1/newtonsoft.json.12.0.1.nupkg`, then reload the Newtonsoft.Json page | 12.0.1 loses the **mirror** label and keeps its publish date from nuget.org (in 2018), not today's |

## Dart feeds

These checks use the Dart feed of the data set. They need PaGetto 1.3.0 or later.

| # | Account | Steps | Expected |
|---|---|---|---|
| D1 | `admin` | Admin > Feeds > **New feed**: slug `dart-test`, type **Dart**; open its **Settings**; delete it afterwards | The feed shows a **Dart** badge and its copy button copies `http://localhost:5000/feeds/dart-test` (no `/v3/index.json`). Settings have no Retention section and no Deletion behavior setting, the mirror URL placeholder is `https://pub.dev` and there is no "Use NuGet V2" checkbox |
| D2 | `alice` | Open Dart (`/feeds/dart/`), then open the feed switcher | "Dart feed" above the feed name (a NuGet feed says "NuGet feed"), and the switcher shows a **Dart** badge on this feed and **NuGet** on the others. 3 packages: contoso_core v2.0.0, contoso_http v1.0.0, contoso_widgets v1.0.0, each with its description and downloads. The search box says "Search packages…", and there are no Type, Framework, Tag or Prerelease filters. The header has no Upload link; **Publish a package** opens Connect. Searching `http` leaves contoso_http |
| D3 | `carol` | Open contoso_http | Version 1.0.0 with a **latest** badge and an `sdk ^3.0.0` chip. Install tabs **pubspec.yaml**, **Dart** and **Flutter**, all naming `http://localhost:5000/feeds/dart`. **Dependencies** lists contoso_core `^2.0.0`, linking to its page. **Versions** lists 1.0.0, 0.9.0+2 and 0.9.0+1 with downloads and dates. **Download archive** downloads `contoso_http-1.0.0.tar.gz` |
| D4 | `carol` | Open the version 0.9.0+1 from the Versions tab | The page shows 0.9.0+1, in the title, the URL (`0.9.0%2B1`) and the install tabs (`^0.9.0+1`), not 0.9.0 or 0.9.0+2 |
| D5 | `carol` | Open contoso_core, then its version 1.0.0 | 1.0.0 is struck through with a **retracted** badge in the Versions tab; its page has the badge and a note. No Manage section and no **Restore** link (no delete permission) |
| D6 | `alice` | contoso_core 1.0.0 > **Restore**; then on 1.1.0 **Retract 1.1.0**, **Restore** it, and finally **Delete 1.1.0** | Each step asks for confirmation where it changes data and shows the result; after the delete the page shows 2.0.0 and 1.1.0 is gone. The audit log has `package_relist_succeeded`, `package_unlist_succeeded` and `package_delete_succeeded` rows for feed `dart`, actor `alice`. Do `down -v` afterwards |
| D7 | `carol` | Dart > **Connect** | Hosted URL `http://localhost:5000/feeds/dart` with a copy button, an "Authentication required" note that links to My Tokens, and the tabs **Sign in** (`dart pub token add …`), **Use a package**, **Publish** and **All packages** |
| D8 | none | `curl -i http://localhost:5000/feeds/dart/api/packages/contoso_core`; the same with `-u carol:<password>`; then with `-u carol:<password>` for `/feeds/dart/v3/index.json` and `/feeds/internal/api/packages/contoso_core` | Without credentials: 401 with `WWW-Authenticate: Bearer realm="pub", message="…dart pub token add http://localhost:5000/feeds/dart"`. With them: 200 and JSON whose `latest.version` is `2.0.0` and whose version 1.0.0 has `"retracted":true`. The NuGet API on the Dart feed and the pub API on a NuGet feed: 404 |
| D9 | none | `curl -i -u bob:<password> http://localhost:5000/feeds/dart/api/packages/versions/new` (pull only), then the same as `build-agent` | bob: 403 with `WWW-Authenticate: Bearer realm="pub", message="You do not have permission to publish packages to this feed."`. build-agent: 200 with the upload `url` |
| D10 | `admin` | `curl -u admin:<password> http://localhost:5000/feeds/dart/api/packages/path` (needs internet), download the `archive_url` of its `latest` version the same way, then reload Dart | The JSON lists pub.dev's versions of `path`, with every `archive_url` on `localhost:5000`. After the download, `path` is in the package list and that version carries a **mirror** label on its page |
| D11 | `admin` | Dart > **Statistics** | 3 packages ("1 unlisted version"), 8 versions ("7 stable · 1 prerelease") and 23 total downloads. **Most downloaded**: contoso_core (14), contoso_http (6), contoso_widgets (3), each linking to its package page |
| D12 | `carol` | Open `/feeds/dart/packages/contoso_core`, `/feeds/dart/upload` and `/feeds/internal/artifacts/contoso_core` | 404 for all three: NuGet pages don't exist on a Dart feed, and the Dart package page doesn't exist on a NuGet feed |

## npm feeds

The data set has no npm feed, so J1 creates one and the later checks use it. They need PaGetto 1.4.0 or later, Node.js with npm, and a personal access token of `admin` from **My Tokens** in place of `<token>`. Run the npm commands in an empty folder with this `.npmrc`:

```ini
registry=http://localhost:5000/feeds/js/npm/
//localhost:5000/feeds/js/npm/:_authToken=<token>
```

| # | Account | Steps | Expected |
|---|---|---|---|
| J1 | `admin` | Admin > Feeds > **New feed**: slug `js`, type **npm**; open its **Settings**; give the group of `carol` Pull on it | The feed shows an **npm** badge and its copy button copies `http://localhost:5000/feeds/js/npm/`. Settings have no Retention section and no Deletion behavior setting, the mirror URL placeholder is `https://registry.npmjs.org` and there is no "Use NuGet V2" checkbox |
| J2 | `admin` | In a folder with a `package.json` named `@contoso/ui` (version `1.0.0`, a description, a README.md), run `npm publish`; then bump to `1.1.0-beta.1` and run `npm publish --tag beta` | Both print `+ @contoso/ui@…`. `npm view @contoso/ui` shows the description and the dist-tags `latest: 1.0.0` and `beta: 1.1.0-beta.1`. `npm publish` of 1.0.0 again fails with "cannot publish over the previously published versions" |
| J3 | `carol` | Open js (`/feeds/js/`), then @contoso/ui | "npm feed" above the feed name and one package @contoso/ui v1.0.0 with a **UI** tile. The package page shows 1.0.0 with a **latest** badge, the readme, install tabs **npm**, **Yarn**, **pnpm** and **package.json**, both versions under **Versions**, and **Download tarball** downloads `ui-1.0.0.tgz` |
| J4 | `admin` | `npm deprecate @contoso/ui@1.0.0 "Use 2.x"`, reload the package page; then `npm deprecate @contoso/ui@1.0.0 ""` | After the first: a **deprecated** badge next to **latest** (1.0.0 stays the latest version, as npm's `latest` tag still points at it, also in the package list) and the note "This version is deprecated: Use 2.x.", and `npm install @contoso/ui@1.0.0` warns with the message. After the second, both are gone |
| J5 | `admin` | `npm dist-tag add @contoso/ui@1.0.0 stable`, `npm dist-tag ls @contoso/ui`, `npm dist-tag rm @contoso/ui stable`, `npm dist-tag rm @contoso/ui latest` | The list has `beta`, `latest` and `stable`; removing `stable` works; removing `latest` fails with "The latest tag can't be removed." |
| J6 | `carol` | js > **Connect** | Registry URL `http://localhost:5000/feeds/js/npm/` with a copy button, an "Authentication required" note that links to My Tokens, a "This feed has no mirror" note, and the tabs **.npmrc** (with `//localhost:5000/feeds/js/npm/:_authToken=YOUR_TOKEN`), **Scoped packages**, **Yarn**, **pnpm** and **Publish** |
| J7 | none | `curl -i http://localhost:5000/feeds/js/npm/@contoso%2fui`; the same with `-u carol:<password>`; then with `-u carol:<password>` for `/feeds/js/v3/index.json` and `/feeds/internal/npm/@contoso%2fui` | Without credentials: 401. With them: 200 and JSON with both versions, each `dist.tarball` on `localhost:5000/feeds/js/npm/@contoso/ui/-/`. The NuGet API on the npm feed and the npm API on a NuGet feed: 404 |
| J8 | `admin` | Add the mirror `https://registry.npmjs.org` to js (needs internet). In a folder with `package.json` `{}`, run `npm install express@4 JSONStream`, then reload js | The install succeeds and every `resolved` URL in `package-lock.json` is on `localhost:5000/feeds/js/npm/`. express, its dependencies and jsonstream are in the package list, and their versions carry a **mirror** label |
| J9 | `admin` | `npm unpublish @contoso/ui@1.1.0-beta.1`, then on the package page of 1.0.0 **Deprecate 1.0.0**, **Undeprecate**, and **Delete 1.0.0**; delete the feed afterwards | The unpublish prints `- @contoso/ui@1.1.0-beta.1` and the version is gone from the page. The page buttons ask for confirmation and work; after the delete the package is gone. The audit log has `package_delete_succeeded`, `package_unlist_succeeded` and `package_relist_succeeded` rows for feed `js`. Do `down -v` afterwards |

## Maven feeds

The data set has no Maven feed, so MV1 creates one and the later checks use it. They need PaGetto 1.5.0 or later, Docker (for Maven and Gradle) and a personal access token of `admin` from **My Tokens** in place of `<token>`. Maven 3.8.1 and later refuse plain HTTP repositories, so the `settings.xml` from the Connect page also needs this mirror, and Gradle needs `isAllowInsecureProtocol = true`; inside a container the server is `host.docker.internal:5000`:

```xml
<mirror><id>maven-default-http-blocker</id><mirrorOf>external:dummy:*</mirrorOf><url>http://0.0.0.0/</url><blocked>false</blocked></mirror>
```

| # | Account | Steps | Expected |
|---|---|---|---|
| MV1 | `admin` | Admin > Feeds > **New feed**: slug `jvm`, type **Maven**; open its **Settings**; give the group of `carol` Pull on it | The feed shows a **Maven** badge and its copy button copies `http://localhost:5000/feeds/jvm/maven2/`. Settings have no Retention section and no Deletion behavior setting, the mirror URL placeholder is `https://repo.maven.apache.org/maven2`, the overwrite policy says "Files of SNAPSHOT versions are always replaced." and the max package size "Applies to each file of a version." |
| MV2 | `admin` | In a Maven project `com.contoso:widgets:1.0.0` with a description, the source plugin and `distributionManagement` to the feed (the **Publish** tab of Connect), run `mvn deploy` | `BUILD SUCCESS`, with `Uploaded to` lines for the JAR, POM, sources JAR and `maven-metadata.xml`. `mvn deploy` again after a code change fails with `status code: 409, reason phrase: widgets-1.0.0.jar already exists in com.contoso:widgets 1.0.0, and the feed does not allow overwriting it.` |
| MV3 | `admin` | Change the version to `1.1.0-SNAPSHOT` and run `mvn deploy` twice; open `/feeds/jvm/maven2/com/contoso/widgets/1.1.0-SNAPSHOT/maven-metadata.xml` | Both deploys succeed, the second with files ending in `-2`. The metadata has `<buildNumber>2</buildNumber>` and a `snapshotVersion` for the JAR, sources JAR and POM, each with a value ending in `-2` |
| MV4 | `admin` | In a Gradle project with the `maven-publish` plugin and the feed as its repository (the **Publish** tab), run `gradle publish` for `com.contoso:gradle-lib:2.0.0` | The publish succeeds, and `/feeds/jvm/maven2/com/contoso/gradle-lib/2.0.0/gradle-lib-2.0.0.module` and `….jar.sha512` download |
| MV5 | `carol` | Open jvm (`/feeds/jvm/`), then com.contoso:widgets | "Maven feed" above the feed name, both packages, and **Install** copies a `<dependency>` element. The package page shows 1.0.0 with a **latest** badge, the description, dependency tabs **Maven**, **Gradle (Kotlin)**, **Gradle (Groovy)** and **sbt**, Packaging `jar`, the POM's dependencies under **Dependencies**, the three files with sizes and SHA-1 under **Files** (each name downloads the file), and 1.0.0 and 1.1.0-SNAPSHOT (marked prerelease) under **Versions** |
| MV6 | `carol` | jvm > **Connect** | Repository URL `http://localhost:5000/feeds/jvm/maven2/` with a copy button, an "Authentication required" note that links to My Tokens, a "This feed has no mirror" note, and the tabs **Maven**, **Maven mirror**, **Gradle**, **sbt** (with `Credentials("PaGetto", "localhost", …)`) and **Publish** |
| MV7 | none | `curl -i http://localhost:5000/feeds/jvm/maven2/com/contoso/widgets/maven-metadata.xml`; the same with `-u carol:<password>`, and with `.sha1` appended; then with `-u carol:<password>` for `/feeds/jvm/v3/index.json` and `/feeds/internal/maven2/com/contoso/widgets/maven-metadata.xml`; finally `curl -i -X PUT -u carol:<password> --data x http://localhost:5000/feeds/jvm/maven2/com/contoso/widgets/2.0/widgets-2.0.jar` | Without credentials: 401 with `WWW-Authenticate: Basic realm="PaGetto"`. With them: 200 and the versions 1.0.0 and 1.1.0-SNAPSHOT, and the `.sha1` is a 40-character checksum. The NuGet API on the Maven feed and the Maven repository on a NuGet feed: 404. The upload: 403 (carol has no push permission) |
| MV8 | `admin` | Add the mirror `https://repo.maven.apache.org/maven2` to jvm (needs internet). In a Maven project that depends on `org.apache.commons:commons-lang3:3.14.0`, use the **Maven mirror** tab of Connect as `settings.xml`, run `mvn -U dependency:tree` with an empty local repository, then reload jvm | The build succeeds, every download is from `localhost:5000/feeds/jvm/maven2/`, and `dependency:tree` lists commons-lang3. commons-lang3 and the Maven plugins are in the package list, and their versions carry a **mirror** label |
| MV9 | `admin` | On the package page of com.contoso:widgets 1.0.0: **Unlist 1.0.0**, reload its `maven-metadata.xml`, **Relist**, then **Delete 1.0.0**; delete the feed afterwards | While unlisted, 1.0.0 is missing from the metadata but its JAR still downloads. The buttons ask for confirmation and work; after the delete the version and its **Files** are gone. The audit log has `package_upload_succeeded`, `package_unlist_succeeded`, `package_relist_succeeded` and `package_delete_succeeded` rows for feed `jvm`. Do `down -v` afterwards |

## Python feeds

The data set has no Python feed, so PY1 creates one and the later checks use it. They need PaGetto 1.6.0 or later, Python 3.9 or later with `pip install build twine uv`, and a personal access token of `admin` from **My Tokens** in place of `<token>`. A test project is a folder with a `README.md`, a package folder and this `pyproject.toml`:

```toml
[build-system]
requires = ["hatchling"]
build-backend = "hatchling.build"

[project]
name = "Contoso.Widgets"
version = "1.0.0"
description = "Widgets for Contoso."
readme = "README.md"
requires-python = ">=3.9"
dependencies = ["requests>=2.0"]

[tool.hatch.build.targets.wheel]
packages = ["src/contoso_widgets"]
```

| # | Account | Steps | Expected |
|---|---|---|---|
| PY1 | `admin` | Admin > Feeds > **New feed**: slug `py`, type **Python**; open its **Settings**; give the group of `carol` Pull on it | The feed shows a **Python** badge and its copy button copies `http://localhost:5000/feeds/py/pypi/simple/`. Settings have no Retention section and no Deletion behavior setting, the mirror URL placeholder is `https://pypi.org/simple` and the max package size "Applies to each file of a version." |
| PY2 | `admin` | In the test project run `python -m build`, then `twine upload --repository-url http://localhost:5000/feeds/py/pypi/legacy/ -u __token__ -p <token> dist/*`; change the code, build again and upload again | The first upload prints `Uploading` for the wheel and the sdist and succeeds. The second fails with `409 Conflict` and `File already exists: contoso_widgets-1.0.0-py3-none-any.whl of contoso-widgets 1.0.0, and the feed does not allow overwriting it.` |
| PY3 | `admin` | Change the version to `1.1.0b1`, then `uv build` and `uv publish --publish-url http://localhost:5000/feeds/py/pypi/legacy/ --username __token__ --password <token>`; run the same `uv publish` again with `--check-url http://localhost:5000/feeds/py/pypi/simple/` | The publish succeeds; the second one prints `already exists, skipping` for both files |
| PY4 | `carol` | Open py (`/feeds/py/`), then Contoso.Widgets | "Python feed" above the feed name, the package as `Contoso.Widgets`, and **Install** copies `pip install Contoso.Widgets==1.0.0`. The package page shows 1.0.0 with a **latest** badge, the description, `Python >=3.9`, the tabs **pip**, **uv**, **Poetry** and **requirements.txt**, the readme, `requests >=2.0` under **Dependencies**, the wheel and sdist with sizes and SHA-256 under **Files** (each name downloads the file, the `.metadata` files are not listed), and 1.0.0 and 1.1.0b1 (marked prerelease) under **Versions**. `/feeds/py/artifacts/contoso-widgets` shows the same page |
| PY5 | `carol` | py > **Connect** | Index URL `http://localhost:5000/feeds/py/pypi/simple/` and upload URL `http://localhost:5000/feeds/py/pypi/legacy/` with copy buttons, an "Authentication required" note that names `__token__` and links to My Tokens, a "This feed has no mirror" note, and the tabs **pip**, **uv**, **Poetry** (with `--priority=primary PyPI`) and **Publish** |
| PY6 | none | `curl -i http://localhost:5000/feeds/py/pypi/simple/contoso-widgets/`; the same with `-u __token__:<token>`, then with `-H "Accept: application/vnd.pypi.simple.v1+json"`, and for `/pypi/simple/Contoso_Widgets/`; then with `-u carol:<password>` for `/feeds/py/v3/index.json` and `/feeds/internal/pypi/simple/` | Without credentials: 401 with `WWW-Authenticate: Basic realm="PaGetto"`. With them: 200 and an HTML page with a link per file, each with `#sha256=` and `data-requires-python="&gt;=3.9"`, and wheels with `data-core-metadata`; the JSON form lists the versions 1.0.0 and 1.1.0b1 and the files; the name that is not normalized answers 301 to `/pypi/simple/contoso-widgets/`. The NuGet API on the Python feed and the index on a NuGet feed: 404 |
| PY7 | `admin` | Add the mirror `https://pypi.org/simple` to py (needs internet). In a new virtual environment run `pip install --index-url http://__token__:<token>@localhost:5000/feeds/py/pypi/simple/ Contoso.Widgets`, then reload py | The install succeeds with Contoso.Widgets 1.0.0 and requests and its dependencies, every download is from `localhost:5000/feeds/py/pypi/files/`, and pip downloads `….whl.metadata` files. requests and its dependencies are in the package list, and their versions carry a **mirror** label |
| PY8 | `admin` | On the package page of Contoso.Widgets 1.0.0: **Yank 1.0.0**, reload `/feeds/py/pypi/simple/contoso-widgets/` and run `pip install "Contoso.Widgets==1.0.0"` as in PY7 in a new virtual environment; **Unyank**, then **Delete 1.0.0**; delete the feed afterwards | While yanked, the links of 1.0.0 carry `data-yanked`, and pip installs the pinned version with a warning that it is yanked. The buttons ask for confirmation and work; after the delete the version and its **Files** are gone. The audit log has `package_upload_succeeded`, `package_unlist_succeeded`, `package_relist_succeeded` and `package_delete_succeeded` rows for feed `py`. Do `down -v` afterwards |

## Rust feeds

The data set has no Rust feed, so CG1 creates one and the later checks use it. They need PaGetto 1.7.0 or later, Cargo 1.74 or later, internet access for crates.io, and a personal access token of `admin` from **My Tokens** in place of `<token>`. The test crate is made with `cargo new --lib contoso_widgets`, gets a `README.md`, and has this `Cargo.toml`:

```toml
[package]
name = "contoso_widgets"
version = "1.0.0"
edition = "2021"
description = "Widgets for Contoso."
license = "MIT"
repository = "https://example.com/contoso/widgets"
keywords = ["widgets"]
rust-version = "1.70"
publish = ["rs"]

[dependencies]
itoa = "1"
```

| # | Account | Steps | Expected |
|---|---|---|---|
| CG1 | `admin` | Admin > Feeds > **New feed**: slug `rs`, type **Rust**; open its **Settings**; give the group of `carol` Pull on it | The feed shows a **Rust** badge and its copy button copies `sparse+http://localhost:5000/feeds/rs/cargo/index/`. Settings have no Retention section and no Deletion behavior setting, and the mirror URL placeholder is `https://index.crates.io` |
| CG2 | `admin` | Put the **Cargo** tab of rs > **Connect** into the test crate's `.cargo/config.toml` (without the `cargo login` lines), run `cargo login --registry rs` and paste `<token>`, then `cargo publish --registry rs`; run `cargo publish --registry rs --allow-dirty` again | The first publish uploads and succeeds. The second fails with `crate contoso_widgets@1.0.0 already exists on `rs` index` |
| CG3 | `admin` | Change the version to `1.1.0-beta.1` and run `cargo publish --registry rs --allow-dirty`; then `cargo search --registry rs widgets` | The publish succeeds. The search prints `contoso_widgets = "1.0.0"` with `# Widgets for Contoso.` |
| CG4 | `carol` | Open rs (`/feeds/rs/`), then contoso_widgets | "Rust feed" above the feed name, and **Install** copies `cargo add contoso_widgets@1.0.0 --registry rs`. The package page shows 1.0.0 with a **latest** badge, the description, `Rust 1.70`, the tabs **Cargo** and **Cargo.toml**, the readme, `itoa ^1` under **Dependencies**, 1.0.0 and 1.1.0-beta.1 (marked prerelease) under **Versions** with **Download** links, the license MIT, a **Source repository** link, the keyword `widgets`, and a **Download crate** button that downloads `contoso_widgets-1.0.0.crate` |
| CG5 | `carol` | rs > **Connect** | Index URL `sparse+http://localhost:5000/feeds/rs/cargo/index/` with a copy button, an "Authentication required" note that links to My Tokens, a "This feed has no mirror" note, and the tabs **Cargo** (with `[registries.rs]`, `credential-provider = ["cargo:token"]`, `cargo login --registry rs` and `CARGO_REGISTRIES_RS_TOKEN`), **Cargo.toml** and **Publish** |
| CG6 | none | `curl -i http://localhost:5000/feeds/rs/cargo/index/config.json`; the same with `-H "Authorization: <token>"`, then for `/feeds/rs/cargo/index/co/nt/contoso_widgets` and `/feeds/rs/cargo/index/contoso_widgets`; then with the token for `/feeds/rs/v3/index.json` and `/feeds/internal/cargo/index/config.json` | Without the token: 401 with `WWW-Authenticate: Cargo login_url="http://localhost:5000/feeds/rs/connect"`. With it: `config.json` has `dl`, `api` and `"auth-required":true`; the index file has one JSON line per version with `cksum` and `"yanked":false`, and `itoa` with `"req":"^1"` and the crates.io index as `registry`; the path without its prefix is 404. The NuGet API on the Rust feed and the registry on a NuGet feed: 404 |
| CG7 | `admin` | Add the mirror `https://index.crates.io` to rs. In a new crate with `contoso_widgets = { version = "1", registry = "rs" }` as its dependency and the **Cargo** tab of Connect with `[source.crates-io]` as its `.cargo/config.toml`, set `CARGO_HOME` to an empty folder, set `CARGO_REGISTRIES_RS_TOKEN=<token>` and run `cargo build`; then reload rs | The build succeeds, and Cargo downloads contoso_widgets and itoa from `localhost:5000/feeds/rs/`. itoa is in the package list, and its version carries a **mirror** label |
| CG8 | `admin` | Run `cargo yank --registry rs contoso_widgets@1.0.0` in the test crate and reload its package page and index file; `cargo yank --registry rs contoso_widgets@1.0.0 --undo`; then on the package page **Yank 1.0.0**, **Unyank**, and **Delete 1.1.0-beta.1**; delete the feed afterwards | While yanked, the page marks 1.0.0 **yanked** and its index line has `"yanked":true`. The buttons ask for confirmation and work; after the delete the version is gone. The audit log has `package_upload_succeeded`, `package_unlist_succeeded`, `package_relist_succeeded` and `package_delete_succeeded` rows for feed `rs`. Do `down -v` afterwards |

## Small screens

| # | Account | Steps | Expected |
|---|---|---|---|
| Y1 | `admin` | Emulate a 375 x 812 viewport, open Internal, a package page, Connect, Upload, Statistics, Admin > Accounts, Admin > Feeds and a feed's settings | No horizontal page scrolling (wide tables scroll inside their box), rows reflow, the navigation and the user menu are collapsed behind the menu button and open as a panel under the header |

## Security

| # | Account | Steps | Expected |
|---|---|---|---|
| Z1 | `admin` | Create an account named `x'+(document.title='pwned')+'`, disable it, open ⋯ > **Delete account…** and cancel | The dialog shows the username literally; the page title doesn't change |
