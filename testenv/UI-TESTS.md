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
| S7 | none | Sign in as `bob` with a wrong password 5 times, then with the right one | "This account is locked due to too many failed attempts" (resets after 15 minutes or with `down -v`) |
| S8 | `alice` | User menu > **Change password**, change it with the wrong current password, then with the right one | Wrong one: "The current password is incorrect."; right one: "Your password has been changed." and signing in again needs the new password |
| S9 | none | Start with `TESTENV_EMPTY=1` and sign in as `admin` / `admin` | Lands on **Change password** with a warning; opening any other page returns there; after changing the password the site works normally |

## Navigation

| # | Account | Steps | Expected |
|---|---|---|---|
| N1 | `admin` | Open the feed switcher (the pill next to the logo) | Default, Internal, Experimental, Archive in that order, each with its description and a check on the current feed |
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
| A6 | `admin` | `bob` > ⋯ > **Reset password…** with a 12+ character password (**Set password** stays disabled below 12), then sign in as `bob` with it | "Password of 'bob' reset successfully.", and the sign-in works (also right after S7's lockout) |
| A7 | `admin` | Look at the account list | Column headers on a desktop screen; each row has the avatar, groups, an Enabled/Disabled and a "Web + API"/"API only" label, the date and a ⋯ button whose menu isn't cut off, also on the last row |
| A8 | `admin` | Open ⋯ > **New token…** on `build-agent` (the last row), press Esc, open it again, type a name and press Enter | A dialog with the cursor in the name field; Esc closes it; Enter creates the token |

## Admin > Groups & Permissions

| # | Account | Steps | Expected |
|---|---|---|---|
| G1 | `admin` | Open Developers > **Feed permissions** | "4 of 4 feeds"; Pull on Default, Internal, Archive; Pull, Push, Delete on Experimental |
| G2 | `admin` | Create a group `Developers`, then `developers` | "already exists" both times |
| G3 | `admin` | Remove `carol` from Developers (× on her chip), then check `carol` | `carol` sees "No feeds available"; add her back with **Add member** (the list only offers accounts that aren't members yet) and she sees all four feeds again |
| G4 | `admin` | Open Developers > **Feed permissions**, clear Pull on Internal and **Save permissions** | The button is disabled until a box changes, then shows "Unsaved changes" next to it (ticking the box again disables it again). After saving, `bob` no longer sees Internal; restore it |
| G5 | `admin` | Do G4, then check the container log | One `AUDIT feed_permission_revoked` line for Internal and one `feed_permission_set` line for the restore, no lines for the unchanged feeds |
| G6 | `admin` | Create a group, open its delete confirmation in two tabs, confirm in both | The first shows the toast "Group deleted successfully.", the second "Group not found." |

## Access control

| # | Account | Steps | Expected |
|---|---|---|---|
| X1 | none | Open `/feeds/internal/packages/Contoso.Logging` | Sign-in prompt, and the page title doesn't name the package |
| X2 | `carol` | Open `/feeds/internal/packages/Contoso.Logging` | No Manage section; a crafted Unlist POST returns 403 |
| X3 | none | `curl -u carol:<password> -X PUT -F package=@testenv/packages/Contoso.Mail.1.0.0.nupkg http://localhost:5000/feeds/internal/api/v2/package` | 403 (the same request without `-u` gets 401) |

## Mirror

| # | Account | Steps | Expected |
|---|---|---|---|
| R1 | `admin` | Open Default > `/packages/Newtonsoft.Json` (needs internet) | The package page with nuget.org's versions |
| R2 | `admin` | Look at the versions | Versions not stored in the feed carry a **mirror** label, no downloads, no `1900-01-01` dates and no Relist; upstream-unlisted versions aren't listed; opening a mirror-only version shows no Manage section |
| R3 | `admin` | Download a mirror-only version, e.g. `curl -u admin:<password> -o NUL http://localhost:5000/v3/package/newtonsoft.json/12.0.1/newtonsoft.json.12.0.1.nupkg`, then reload the Newtonsoft.Json page | 12.0.1 loses the **mirror** label and keeps its publish date from nuget.org (in 2018), not today's |

## Small screens

| # | Account | Steps | Expected |
|---|---|---|---|
| Y1 | `admin` | Emulate a 375 x 812 viewport, open Internal, a package page, Connect, Upload, Statistics, Admin > Accounts, Admin > Feeds and a feed's settings | No horizontal page scrolling (wide tables scroll inside their box), rows reflow, the navigation and the user menu are collapsed behind the menu button and open as a panel under the header |

## Security

| # | Account | Steps | Expected |
|---|---|---|---|
| Z1 | `admin` | Create an account named `x'+(document.title='pwned')+'`, disable it, open ⋯ > **Delete account…** and cancel | The dialog shows the username literally; the page title doesn't change |
