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

## OpenID Connect sign-in

These checks need a Keycloak with the realm in `keycloak/pagetto-realm.json` (users `alice`, in the Keycloak groups `Admin` and `developers`, `bob` in `developers` without an email, and `admin`; their test passwords are in that file) and a PaGetto in `Hybrid` mode that uses it. Start both on one network; the browser and PaGetto reach Keycloak as `host.docker.internal:8080`:

```bash
docker network create pagetto-oidc
docker run -d --name kc-oidc --network pagetto-oidc -p 8080:8080 \
  -e KC_BOOTSTRAP_ADMIN_USERNAME=kcadmin -e KC_BOOTSTRAP_ADMIN_PASSWORD=kcadmin \
  -e KC_HOSTNAME=http://host.docker.internal:8080 \
  -v "$PWD/testenv/keycloak:/opt/keycloak/data/import" quay.io/keycloak/keycloak:26.0 start-dev --import-realm
docker run -d --name pagetto-oidc --network pagetto-oidc -p 5000:8080 \
  -e Authentication__Mode=Hybrid \
  -e Authentication__Oidc__Authority=http://host.docker.internal:8080/realms/pagetto \
  -e Authentication__Oidc__ClientId=pagetto -e Authentication__Oidc__ClientSecret=pagetto-test-secret \
  -e Authentication__Oidc__DisplayName=Keycloak -e Authentication__Oidc__RequireHttpsMetadata=false \
  letreset/pagetto:dev
```

| # | Account | Steps | Expected |
|---|---|---|---|
| O1 | none | Open `/Login` | "Use your Keycloak or local account to continue.", a **Sign in with Keycloak** button and the local sign-in form |
| O2 | none | **Sign in with Keycloak**, sign in as `alice` | Back on PaGetto, signed in. Admin > Accounts lists `alice@example.test` with the **Admin** badge and "Alice Admin · OIDC · alice@example.test" |
| O3 | `alice` | Admin > Groups & permissions: create the group `Developers` with the app role value `developers`. Then sign in as `bob` through Keycloak in another context and reload the groups page as `alice` | `Developers` shows "Synced · developers" and the member `bob` with a **Synced** badge and no remove button; **Add member** offers only local accounts |
| O4 | none | **Sign in with Keycloak** as the Keycloak user `admin` | Back on `/Login` with "authentication_failed": the username `admin` belongs to the local account. The server log says "Login denied: the username admin of the new Oidc user is already used by another account" |
| O5 | `bob` | User menu > **Change password** | "Your account signs in through single sign-on, so its password is managed there." |
| O6 | `alice` | **Connect**, then **My Tokens** > create a token and run `curl -u alice@example.test:<token> 'http://localhost:5000/v3/search?q=a'` | Connect says "Keycloak users: sign in to the web UI to generate a personal access token (PAT)..."; curl answers 200, and 401 with a wrong token |

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
| P19 | `admin` | Contoso.Logging > **Download SBOM** in the sidebar; then `curl -u carol:<password> http://localhost:5000/feeds/internal/api/v1/sbom/Contoso.Logging/2.0.0` | Downloads `contoso.logging-2.0.0.cdx.json`, a CycloneDX 1.6 document whose `metadata.component` is Contoso.Logging 2.0.0 (`pkg:nuget/Contoso.Logging@2.0.0`) and whose `components` include Contoso.Core with its version range per target framework; curl returns the same document, and without `-u` it gets 401 |

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
| C2b | `carol` | On **My Tokens**, create a token with **Access** Read only and **Feeds** Experimental, push a package to Experimental with it, then search Internal with it (`/feeds/internal/v3/search`) | The **Feeds** list has only the feeds `carol` can read; the token's row says "Read only · Experimental"; the push is refused with 403 although `carol` may push to Experimental, and the search of Internal gets 404 |
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
| FS9 | `admin` | On a feed that is not a NuGet feed (e.g. npm), clear **Use global default** for Max major versions and for **Unused mirrored versions**, enter `2` and `30`, set the **Webhook** URL `https://ci.example.com/hooks/packages` and a secret, save and reload | The retention text says limits are applied within an hour; the values stay after the reload; the URL is kept, the secret field is empty with "Leave blank to keep existing" and a **Remove the secret** box. A webhook URL `ftp://example.com/hook` is refused with "must be an absolute http or https URL" and nothing is saved |

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

## Admin > Vulnerabilities

These need internet access to api.osv.dev, and a minute after the environment starts for the first scan.

| # | Account | Steps | Expected |
|---|---|---|---|
| V1 | `admin` | Default > `/packages/Newtonsoft.Json/12.0.1` (stores the version from nuget.org), **Download .nupkg**; wait a minute and reload | A "1 known vulnerability in this version" card above the install tabs with a **High** badge, `GHSA-5crp-9r3c-p9vr` linked to osv.dev, its CVE alias, the affected versions and "Fixed in **13.0.1**" |
| V2 | `admin` | Open **Vulnerabilities** in the admin tabs (also in the user menu) | The Newtonsoft.Json 12.0.1 row with its feed `default`, linked to its package page; Severity = Critical hides it, **Clear** shows it again |
| V3 | `admin` | **Export CSV** and **Export JSON** | `vulnerabilities.csv` with a header row and the Newtonsoft.Json row; `vulnerabilities.json` with the same finding |
| V4 | `alice` | Open `/admin/vulnerabilities` | Redirected to the package list |
| V5 | `admin` | Internal > Contoso.Logging | No vulnerability card (OSV doesn't know the package) |

## Admin > Malicious packages

M1 and M2 need internet access to osv-vulnerabilities.storage.googleapis.com, and a minute after the environment starts for the first copy (the Default feed mirrors nuget.org).

| # | Account | Steps | Expected |
|---|---|---|---|
| M1 | `admin` | Open **Malicious packages** in the admin tabs (also in the user menu) | The **Local copy** table has a `NuGet` row with a package count and the refresh time, or "Waiting for first copy" in the first minute |
| M2 | `admin` | **Look up a package** with an ecosystem and a name from the [OpenSSF data](https://github.com/ossf/malicious-packages/tree/main/osv/malicious) (e.g. NuGet and a name under `osv/malicious/nuget/`) | The advisories that list it, linked to osv.dev, with "every version" or "some versions" |
| M3 | `admin` | Allow npm `evil-pkg` version `1.0.0` without a reason, then with the reason `False positive`; then **Remove** it and confirm | First "Say why the package is allowed."; then the toast "npm:evil-pkg@1.0.0 is allowed." and the row with `admin` and the date; after **Remove** the toast "npm:evil-pkg@1.0.0 is no longer allowed." and "No packages are allowed." **Audit log** has `malicious_package_allowed` and `malicious_package_allowance_removed` for `npm:evil-pkg@1.0.0` |
| M4 | `admin` | Admin > Feeds > Default > settings, **Mirrors** section | A **Block malicious packages** switch with "Use global default · Enabled"; the Dart feed has it too; saving with the default unchecked and the switch off keeps it off after a reload |
| M5 | `alice` | Open `/admin/maliciouspackages` | Redirected to the package list |

## Admin > Quarantine and promotion

| # | Account | Steps | Expected |
|---|---|---|---|
| Q1 | `admin` | Admin > Feeds > Internal > settings, **Mirrors** section | **Quarantine vulnerable versions** (Off, Critical, High or above, Moderate or above, Any severity) and a **Quarantine malicious packages** switch; both keep their values after **Save** and a reload |
| Q2 | `admin` | Turn **Quarantine malicious packages** on for Internal; on Malicious packages allow nothing; add a listing for a test package through the database or pick one the data lists; push that package to Internal | The push succeeds, but the package is not in Internal's list and its URL answers 404. **Quarantine** in the admin tabs (also in the user menu) lists it with feed Internal and the reason `Listed as a malicious package (MAL-…)` |
| Q3 | `admin` | On **Quarantine**, **Release** the version of Q2 and confirm; turn **Quarantine malicious packages** on for Experimental, promote the released version there (`POST /feeds/internal/api/v1/promote` with admin's credentials and `targetFeed` `experimental`) and **Delete** the quarantined copy | The toast says it is released, the row disappears and the package is listed and downloads again. The promotion answers 200 and the copy is listed on **Quarantine** for Experimental (the target's policy applies). After **Delete** the row disappears and the copy is gone. **Quarantine history** opens the audit log with `package_quarantined`; the log also has `package_released` and `package_quarantine_deleted` |
| Q4 | `admin` | Set **Quarantine vulnerable versions** to High or above on Internal and push a new version of Contoso.Logging; wait a minute and reload **Quarantine** | First the version is listed with "Waiting for its first vulnerability scan."; after the scan it is gone from the list and served |
| Q5 | `alice` | Open Internal > Contoso.Logging, then **Promote** on the side; choose Experimental, **Promote**; promote it again | The card "Promote" is shown (alice can push to Experimental); the form lists only NuGet feeds alice can push to. "is copied to feed Experimental" with a link to the copy, which has the same version. The second time: "already has version" |
| Q6 | `carol` | Open Experimental > Contoso.Preview.Ai | No **Promote** card (carol can't push to another NuGet feed) |
| Q7 | `carol` | Internal > Contoso.Core > **Promote**, choose Experimental, check **Move**, **Promote** | "moving it needs the delete permission on this feed"; nothing changes |
| Q8 | `alice` | Open `/admin/quarantine` | Redirected to the package list |

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
| D1 | `admin` | Admin > Feeds > **New feed**: slug `dart-test`, type **Dart**; open its **Settings**; delete it afterwards | The feed shows a **Dart** badge and its copy button copies `http://localhost:5000/feeds/dart-test` (no `/v3/index.json`). Settings have a Retention section whose limits say "Unlimited" rather than the global NuGet values, and no Deletion behavior setting, the mirror URL placeholder is `https://pub.dev` and there is no "Use NuGet V2" checkbox |
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
| J1 | `admin` | Admin > Feeds > **New feed**: slug `js`, type **npm**; open its **Settings**; give the group of `carol` Pull on it | The feed shows an **npm** badge and its copy button copies `http://localhost:5000/feeds/js/npm/`. Settings have a Retention section whose limits say "Unlimited" rather than the global NuGet values, and no Deletion behavior setting, the mirror URL placeholder is `https://registry.npmjs.org` and there is no "Use NuGet V2" checkbox |
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
| MV1 | `admin` | Admin > Feeds > **New feed**: slug `jvm`, type **Maven**; open its **Settings**; give the group of `carol` Pull on it | The feed shows a **Maven** badge and its copy button copies `http://localhost:5000/feeds/jvm/maven2/`. Settings have a Retention section whose limits say "Unlimited" rather than the global NuGet values, and no Deletion behavior setting, the mirror URL placeholder is `https://repo.maven.apache.org/maven2`, the overwrite policy says "Files of SNAPSHOT versions are always replaced." and the max package size "Applies to each file of a version." |
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
| PY1 | `admin` | Admin > Feeds > **New feed**: slug `py`, type **Python**; open its **Settings**; give the group of `carol` Pull on it | The feed shows a **Python** badge and its copy button copies `http://localhost:5000/feeds/py/pypi/simple/`. Settings have a Retention section whose limits say "Unlimited" rather than the global NuGet values, and no Deletion behavior setting, the mirror URL placeholder is `https://pypi.org/simple` and the max package size "Applies to each file of a version." |
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
| CG1 | `admin` | Admin > Feeds > **New feed**: slug `rs`, type **Rust**; open its **Settings**; give the group of `carol` Pull on it | The feed shows a **Rust** badge and its copy button copies `sparse+http://localhost:5000/feeds/rs/cargo/index/`. Settings have a Retention section whose limits say "Unlimited" rather than the global NuGet values, and no Deletion behavior setting, and the mirror URL placeholder is `https://index.crates.io` |
| CG2 | `admin` | Put the **Cargo** tab of rs > **Connect** into the test crate's `.cargo/config.toml` (without the `cargo login` lines), run `cargo login --registry rs` and paste `<token>`, then `cargo publish --registry rs`; run `cargo publish --registry rs --allow-dirty` again | The first publish uploads and succeeds. The second fails with `crate contoso_widgets@1.0.0 already exists on `rs` index` |
| CG3 | `admin` | Change the version to `1.1.0-beta.1` and run `cargo publish --registry rs --allow-dirty`; then `cargo search --registry rs widgets` | The publish succeeds. The search prints `contoso_widgets = "1.0.0"` with `# Widgets for Contoso.` |
| CG4 | `carol` | Open rs (`/feeds/rs/`), then contoso_widgets | "Rust feed" above the feed name, and **Install** copies `cargo add contoso_widgets@1.0.0 --registry rs`. The package page shows 1.0.0 with a **latest** badge, the description, `Rust 1.70`, the tabs **Cargo** and **Cargo.toml**, the readme, `itoa ^1` under **Dependencies**, 1.0.0 and 1.1.0-beta.1 (marked prerelease) under **Versions** with **Download** links, the license MIT, a **Source repository** link, the keyword `widgets`, and a **Download crate** button that downloads `contoso_widgets-1.0.0.crate` |
| CG5 | `carol` | rs > **Connect** | Index URL `sparse+http://localhost:5000/feeds/rs/cargo/index/` with a copy button, an "Authentication required" note that links to My Tokens, a "This feed has no mirror" note, and the tabs **Cargo** (with `[registries.rs]`, `credential-provider = ["cargo:token"]`, `cargo login --registry rs` and `CARGO_REGISTRIES_RS_TOKEN`), **Cargo.toml** and **Publish** |
| CG6 | none | `curl -i http://localhost:5000/feeds/rs/cargo/index/config.json`; the same with `-H "Authorization: <token>"`, then for `/feeds/rs/cargo/index/co/nt/contoso_widgets` and `/feeds/rs/cargo/index/contoso_widgets`; then with the token for `/feeds/rs/v3/index.json` and `/feeds/internal/cargo/index/config.json` | Without the token: 401 with `WWW-Authenticate: Cargo login_url="http://localhost:5000/feeds/rs/connect"`. With it: `config.json` has `dl`, `api` and `"auth-required":true`; the index file has one JSON line per version with `cksum` and `"yanked":false`, and `itoa` with `"req":"^1"` and the crates.io index as `registry`; the path without its prefix is 404. The NuGet API on the Rust feed and the registry on a NuGet feed: 404 |
| CG7 | `admin` | Add the mirror `https://index.crates.io` to rs. In a new crate with `contoso_widgets = { version = "1", registry = "rs" }` as its dependency and the **Cargo** tab of Connect with `[source.crates-io]` as its `.cargo/config.toml`, set `CARGO_HOME` to an empty folder, set `CARGO_REGISTRIES_RS_TOKEN=<token>` and run `cargo build`; then reload rs | The build succeeds, and Cargo downloads contoso_widgets and itoa from `localhost:5000/feeds/rs/`. itoa is in the package list, and its version carries a **mirror** label |
| CG8 | `admin` | Run `cargo yank --registry rs contoso_widgets@1.0.0` in the test crate and reload its package page and index file; `cargo yank --registry rs contoso_widgets@1.0.0 --undo`; then on the package page **Yank 1.0.0**, **Unyank**, and **Delete 1.1.0-beta.1**; delete the feed afterwards | While yanked, the page marks 1.0.0 **yanked** and its index line has `"yanked":true`. The buttons ask for confirmation and work; after the delete the version is gone. The audit log has `package_upload_succeeded`, `package_unlist_succeeded`, `package_relist_succeeded` and `package_delete_succeeded` rows for feed `rs`. Do `down -v` afterwards |

## Go feeds

The data set has no Go feed, so GO1 creates one and the later checks use it. They need Go 1.21 or later, Git, internet access for proxy.golang.org, and a personal access token of `admin` from **My Tokens** in place of `<token>`. The go command only sends credentials over HTTPS, so its checks go through a TLS proxy on the test environment's network, with the go command in a container that trusts the proxy's certificate:

```bash
docker run -d --name go-tls --network pagetto-test_default --network-alias pagetto.test caddy:2 caddy reverse-proxy --from https://pagetto.test --to pagetto:8080 --internal-certs
docker cp go-tls:/data/caddy/pki/authorities/local/root.crt ./root.crt
docker run --rm -it --network pagetto-test_default -v "$PWD:/work" -w /work -e SSL_CERT_FILE=/work/root.crt -e NETRC=/work/netrc golang:1.25 bash
```

`netrc` holds `machine pagetto.test login __token__ password <token>`. The test module is a Git repository with this `go.mod`, a `widgets.go` with `package widgets` and a `README.md`, tagged `v1.0.0`:

```
module example.com/Contoso/widgets

go 1.22

require rsc.io/quote v1.5.2
```

| # | Account | Steps | Expected |
|---|---|---|---|
| GO1 | `admin` | Admin > Feeds > **New feed**: slug `golang`, type **Go**; open its **Settings**; give the group of `carol` Pull on it | The feed shows a **Go** badge and its copy button copies `http://localhost:5000/feeds/golang/go`. Settings have a Retention section whose limits say "Unlimited" rather than the global NuGet values, and no Deletion behavior setting, the mirror URL placeholder is `https://proxy.golang.org`, and the mirror text speaks of module proxies |
| GO2 | `admin` | In the test module run `git archive --format=zip --prefix=example.com/Contoso/widgets@v1.0.0/ -o module.zip v1.0.0`, then the `curl` of the **Upload** tab of golang > **Connect** with `admin` and `<token>` and the path `example.com/!contoso/widgets/@v/v1.0.0.zip`; run it again | The first upload answers 201. The second answers 409 with `Version v1.0.0 of module example.com/Contoso/widgets already exists.` |
| GO3 | `admin` | Upload the same zip to `.../@v/v1.0.1.zip`, and a zip made with `--prefix=example.com/other@v1.0.0/` to `.../@v/v1.0.0.zip` | Both answer 400: the first says the files are not in the folder `example.com/Contoso/widgets@v1.0.1/`, the second the same for its folder |
| GO4 | none | In the go container, in a new module with `GOPROXY=https://pagetto.test/feeds/golang/go,https://proxy.golang.org,direct` and `GONOSUMDB=example.com/*`: `go get example.com/Contoso/widgets@latest`, `go build ./...`, `go mod verify`, `go list -m -versions example.com/Contoso/widgets` | The module is added at v1.0.0, rsc.io/quote comes from proxy.golang.org, `go mod verify` prints `all modules verified`, and the version list is `v1.0.0` |
| GO5 | `carol` | Open golang (`/feeds/golang/`), then example.com/Contoso/widgets | "Go feed" above the feed name, and **Install** copies `go get example.com/Contoso/widgets@v1.0.0`. The package page shows v1.0.0 with a **latest** badge, `Go 1.22`, the tabs **go get** and **go.mod** (`require example.com/Contoso/widgets v1.0.0`), the readme, `rsc.io/quote v1.5.2` under **Dependencies**, v1.0.0 under **Versions** with a **Download** link, and a **Download zip** button that downloads the zip from `/feeds/golang/go/example.com/!contoso/widgets/@v/v1.0.0.zip` |
| GO6 | `carol` | golang > **Connect** | GOPROXY URL `http://localhost:5000/feeds/golang/go` with a copy button, an "Authentication required" note that says credentials go over HTTPS only and links to My Tokens, a "This feed has no mirror" note, and the tabs **Go** (with `go env -w GOPROXY=...,https://proxy.golang.org,direct`, `GONOSUMDB` and the `.netrc` lines), **go get** and **Upload** |
| GO7 | none | `curl -i http://localhost:5000/feeds/golang/go/example.com/!contoso/widgets/@v/list`; the same with `-u __token__:<token>`, then for `@latest`, `@v/v1.0.0.info`, `@v/v1.0.0.mod` and `example.com/Contoso/widgets/@v/list`; then with the token for `/feeds/golang/v3/index.json` and `/feeds/internal/go/example.com/!contoso/widgets/@v/list` | Without the token: 401 with `WWW-Authenticate: Basic realm="PaGetto"`. With it: the list is `v1.0.0`, `@latest` and `.info` are `{"Version":"v1.0.0","Time":"…"}`, `.mod` is the `go.mod` of the module, and the unescaped path is 404. The NuGet API on the Go feed and the proxy on a NuGet feed: 404 |
| GO8 | `admin` | Add the mirror `https://proxy.golang.org` to golang. In the go container, with a new module cache (`GOMODCACHE=/tmp/mod`), `GOPROXY=https://pagetto.test/feeds/golang/go` and `GONOSUMDB=example.com/*`, run `go build ./...` in the module of GO4; reload golang; then on the package page of example.com/Contoso/widgets **Unlist v1.0.0**, reload the `@v/list` of GO7, **Relist**, and **Delete v1.0.0**; delete the feed afterwards | The build succeeds, rsc.io/quote and its dependencies are downloaded from `pagetto.test/feeds/golang/go/` and checked against sum.golang.org, and they are in the package list; on their package pages the version has a **mirror** label. While unlisted, the list is empty and the version is marked **unlisted**. The buttons ask for confirmation and work. The audit log has `package_upload_succeeded`, `package_unlist_succeeded`, `package_relist_succeeded` and `package_delete_succeeded` rows for feed `golang`. Do `down -v` and `docker rm -f go-tls` afterwards |

## Ruby feeds

The data set has no Ruby feed, so RG1 creates one and the later checks use it. They need internet access for rubygems.org, and a personal access token of `admin` from **My Tokens** in place of `<token>`. The Ruby clients run in a container on the test environment's network, where the server is `pagetto:8080`:

```bash
docker run --rm -it --network pagetto-test_default -v "$PWD:/work" -w /work ruby:3.4-slim bash
```

The test gem is a folder with `lib/contoso_widgets.rb` (`module ContosoWidgets; end`), a `README.md` and this `contoso_widgets.gemspec`:

```ruby
Gem::Specification.new do |s|
  s.name = "contoso_widgets"
  s.version = ENV.fetch("VERSION", "1.0.0")
  s.platform = ENV.fetch("PLAT", Gem::Platform::RUBY)
  s.summary = "Contoso widgets"
  s.authors = ["Contoso"]
  s.license = "MIT"
  s.homepage = "https://example.com/widgets"
  s.files = ["lib/contoso_widgets.rb", "README.md"]
  s.required_ruby_version = ">= 3.0"
  s.add_dependency "rack", "~> 3.0"
end
```

| # | Account | Steps | Expected |
|---|---|---|---|
| RG1 | `admin` | Admin > Feeds > **New feed**: slug `gems`, type **Ruby**; open its **Settings**; give the group of `carol` Pull on it | The feed shows a **Ruby** badge and its copy button copies `http://localhost:5000/feeds/gems/rubygems/`. Settings have a Retention section whose limits say "Unlimited" rather than the global NuGet values, and no Deletion behavior setting, Max package size says it applies to each file, the mirror URL placeholder is `https://rubygems.org`, and the mirror text speaks of gem servers with a compact index |
| RG2 | `admin` | In the container, `gem build contoso_widgets.gemspec` and `PLAT=java gem build contoso_widgets.gemspec`; with `GEM_HOST_API_KEY=<token>`, `gem push` both with `--host http://pagetto:8080/feeds/gems/rubygems`; push the first again; change the README, build it again and push it | The pushes print `Successfully registered gem: contoso_widgets (1.0.0)` and `(1.0.0-java)`; pushing the same file again succeeds; the changed gem answers `Repushing of gem versions is not allowed: ...` |
| RG3 | `admin` | Push a gem with the token of `carol`, and a file that is not a gem (`gem push README.md ...`) with the token of `admin` | `carol`: `You do not have permission to change gems of this feed.` The file: `The gem is not a valid tar archive.` |
| RG4 | none | In the container, without a mirror on gems: a `Gemfile` with `source "https://rubygems.org"` and `source "http://pagetto:8080/feeds/gems/rubygems/" do gem "contoso_widgets" end`; `bundle config set --global pagetto __token__:<token>`; `bundle install`; then `gem install contoso_widgets --source http://__token__:<token>@pagetto:8080/feeds/gems/rubygems/` | Bundler installs contoso_widgets 1.0.0 from the feed and rack from rubygems.org; `gem install` installs contoso_widgets and rack |
| RG5 | `carol` | Open gems (`/feeds/gems/`), then contoso_widgets | "Ruby feed" above the feed name, and **Install** copies `bundle add contoso_widgets --version "1.0.0" --source "http://localhost:5000/feeds/gems/rubygems/"`. The package page shows v1.0.0 with a **latest** badge, `Ruby >= 3.0`, the tabs **Bundler**, **Gemfile** and **gem install**, the readme, `rack ~> 3.0` under **Dependencies**, `contoso_widgets-1.0.0.gem` and `contoso_widgets-1.0.0-java.gem` with their SHA-256 under **Files**, MIT, and **Homepage** |
| RG6 | `carol` | gems > **Connect** | Gem source URL `http://localhost:5000/feeds/gems/rubygems/` with a copy button, an "Authentication required" note that links to My Tokens, a "This feed has no mirror" note, and the tabs **Bundler** (a `Gemfile` with rubygems.org and a source block, and `bundle config set --global localhost __token__:YOUR_TOKEN`), **gem** and **Push** (`GEM_HOST_API_KEY`, `gem push ... --host http://localhost:5000/feeds/gems/rubygems` and `gem yank`) |
| RG7 | none | `curl -i http://localhost:5000/feeds/gems/rubygems/versions`; the same with `-u __token__:<token>`, then for `info/contoso_widgets`, `names` and `specs.4.8.gz`; then with the token for `/feeds/gems/v3/index.json` and `/feeds/internal/rubygems/versions` | Without the token: 401 with `WWW-Authenticate: Basic realm="PaGetto"`. With it: `/versions` lists `contoso_widgets 1.0.0,1.0.0-java` with an MD5, `info` has a line per platform with `rack:~> 3.0` and `checksum:`, `names` lists contoso_widgets, and `specs.4.8.gz` is 404. The NuGet API on the Ruby feed and the gem server on a NuGet feed: 404 |
| RG8 | `admin` | Add the mirror `https://rubygems.org` to gems. In a new container, a `Gemfile` with only `source "http://pagetto:8080/feeds/gems/rubygems/"`, `gem "contoso_widgets"` and `gem "nokogiri"`, and the `bundle config` of RG4: `bundle install`; reload gems; then `gem yank contoso_widgets -v 1.0.0 --host ...` and reload the `info` of RG7 and the package page; **Unyank 1.0.0**, and **Delete 1.0.0**; delete the feed afterwards | Bundler installs everything from the feed, nokogiri as its platform gem (e.g. `x86_64-linux-gnu`). rack and nokogiri are in the package list, and their versions carry a **mirror** label. After the yank, `gem yank` prints `Successfully yanked gem: contoso_widgets (1.0.0)`, the `info` file is empty after its `---`, and the page marks 1.0.0 **yanked**. The buttons ask for confirmation and work. The audit log has `package_upload_succeeded`, `package_unlist_succeeded`, `package_relist_succeeded` and `package_delete_succeeded` rows for feed `gems`. Do `down -v` afterwards |

## Hex feeds

The data set has no Hex feed, so HX1 creates one and the later checks use it. They need internet access for hex.pm, and a personal access token of `admin` from **My Tokens** in place of `<token>`. Mix and Rebar3 run in a container on the test environment's network, where the server is `pagetto:8080`:

```bash
docker run --rm -it --network pagetto-test_default -v "$PWD:/work" -w /work elixir:1.18 bash
```

In the container, run `mix local.hex --force` and `mix local.rebar --force` first. The test package is a Mix project `contoso_widgets` (`mix new contoso_widgets`) with `description: "Contoso widgets"`, `package: [licenses: ["MIT"], links: %{"GitHub" => "https://example.com/widgets"}]`, `deps: [{:jason, "~> 1.4"}]` and a `README.md`, and a version taken from `System.get_env("VERSION", "1.0.0")`.

| # | Account | Steps | Expected |
|---|---|---|---|
| HX1 | `admin` | Admin > Feeds > **New feed**: slug `elixir`, type **Hex**; open its **Settings**; give the group of `carol` Pull on it | The feed shows a **Hex** badge and its copy button copies `http://localhost:5000/feeds/elixir/hex`. Settings have a Retention section whose limits say "Unlimited" rather than the global NuGet values, and no Deletion behavior setting, the mirror URL placeholder is `https://repo.hex.pm`, and the mirror text speaks of Hex repositories and the feed's own key |
| HX2 | `carol` | elixir > **Connect** | Repository URL `http://localhost:5000/feeds/elixir/hex`, repository name `elixir` and a `SHA256:` fingerprint, each with a copy button; a "Signed registry" note, an "Authentication required" note that links to My Tokens, a "This feed has no mirror" note, and the tabs **Mix** (`mix hex.repo add elixir ... --fetch-public-key SHA256:... --auth-key YOUR_TOKEN` and a `repo: "elixir"` dependency), **Rebar3** (a `rebar.config` with the PEM public key) and **Publish** (`HEX_API_URL`, `HEX_API_KEY`, `mix hex.publish package` and `mix hex.retire`) |
| HX3 | `admin` | In the container, `mix hex.repo add elixir http://pagetto:8080/feeds/elixir/hex --fetch-public-key <fingerprint of HX2> --auth-key <token>`; in contoso_widgets, `mix deps.get`, then with `HEX_API_URL=http://pagetto:8080/feeds/elixir/hex/api` and `HEX_API_KEY=<token>` run `mix hex.publish package` and answer `y`; publish again; publish `VERSION=1.1.0`; publish with `HEX_API_KEY` of `carol` | The repository is added. The first publish prints `Package published to http://pagetto:8080/feeds/elixir/artifacts/contoso_widgets/1.0.0`; the second `Version 1.0.0 of package contoso_widgets already exists.`; 1.1.0 is published; `carol`: `You do not have permission to change packages of this feed.` |
| HX4 | none | In a new Mix project with `{:contoso_widgets, "~> 1.0", repo: "elixir"}` and the repository of HX3: `mix deps.get`; then `mix hex.retire contoso_widgets 1.1.0 security --message "Use 1.0.0"` (with the variables of HX3), `rm -rf deps mix.lock` and `mix deps.get` again; then `mix hex.retire contoso_widgets 1.1.0 --unretire` | Mix gets contoso_widgets 1.1.0 from the feed and jason from hex.pm. After the retirement Mix shows `contoso_widgets 1.1.0 RETIRED! (security) Use 1.0.0`, and the package page marks 1.1.0 **retired**; after `--unretire` the mark is gone |
| HX5 | `carol` | Open elixir (`/feeds/elixir/`), then contoso_widgets | "Hex feed" above the feed name, and **Install** copies `{:contoso_widgets, "~> 1.1.0", repo: "elixir"}`. The package page shows v1.1.0 with a **latest** badge, the tabs **Mix** and **rebar.config**, the readme, `jason ~> 1.4` under **Dependencies**, MIT, **GitHub**, and **Download tarball** |
| HX6 | none | `curl -i http://localhost:5000/feeds/elixir/hex/packages/contoso_widgets`; the same with `-H "Authorization: <token>"`, then for `names`, `public_key` and `tarballs/contoso_widgets-1.0.0.tar`; then with the token for `/feeds/elixir/v3/index.json` and `/feeds/internal/hex/names` | Without the token: 401 with `WWW-Authenticate: Basic realm="PaGetto"`. With it: the registry files are `application/octet-stream` with an `ETag`, `public_key` is a `BEGIN PUBLIC KEY` PEM, and the tarball has an `ETag`. The NuGet API on the Hex feed and the repository on a NuGet feed: 404 |
| HX7 | `admin` | Add the mirror `https://repo.hex.pm` to elixir. In a new container, add the repository as in HX3, and in a Mix project with `{:plug, "~> 1.16", repo: "elixir"}` run `mix deps.get` and `mix compile`; with `mix local.rebar`'s rebar3, build an Erlang project whose `rebar.config` has `{deps, [jsx]}` and the repository of the Connect page's **Rebar3** tab with `{repos, replace, [...]}`; reload elixir; on the package page of contoso_widgets **Retire 1.0.0**, **Unretire 1.0.0** and **Delete 1.0.0**; delete the feed afterwards | Mix gets plug, mime, plug_crypto and telemetry from the feed, and Rebar3 gets jsx from it. They are in the package list, and their versions carry a **mirror** label. The buttons ask for confirmation and work. The audit log has `package_upload_succeeded`, `package_unlist_succeeded`, `package_relist_succeeded` and `package_delete_succeeded` rows for feed `elixir`. Do `down -v` afterwards |

## PHP feeds

The data set has no PHP feed, so CP1 creates one and the later checks use it. They need internet access for Packagist and GitHub, and a personal access token of `admin` from **My Tokens** in place of `<token>`. Composer runs in a container on the test environment's network, where the server is `pagetto:8080`:

```bash
docker run --rm -it --network pagetto-test_default -v "$PWD:/work" -w /work composer:2 sh
```

The test package is a folder with a `composer.json` (`"name": "contoso/widgets"`, `"description": "Contoso widgets"`, `"license": "MIT"`, `"keywords": ["widgets"]`, `"require": {"php": ">=8.1", "psr/log": "^3.0"}`, `"autoload": {"psr-4": {"Contoso\\Widgets\\": "src/"}}` and `"support": {"source": "https://example.com/widgets"}`), a class `Contoso\Widgets\Widget` in `src/Widget.php` and a `README.md`. A consumer project uses the commands of the Connect page's **Composer** tab with `http://pagetto:8080/feeds/php/composer`, and `composer config http-basic.pagetto:8080 __token__ <token>`.

| # | Account | Steps | Expected |
|---|---|---|---|
| CP1 | `admin` | Admin > Feeds > **New feed**: slug `php`, type **PHP**; open its **Settings**; give the group of `carol` Pull on it | The feed shows a **PHP** badge and its copy button copies `http://localhost:5000/feeds/php/composer`. Settings have a Retention section whose limits say "Unlimited" rather than the global NuGet values, and no Deletion behavior setting, the overwrite policy says development versions are always replaced, the mirror URL placeholder is `https://repo.packagist.org`, and the mirror text speaks of Composer repositories and passed-through branches |
| CP2 | `carol` | php > **Connect** | Repository URL `http://localhost:5000/feeds/php/composer` with a copy button, an "Authentication required" note that links to My Tokens, a "This feed has no mirror" note, and the tabs **composer.json** (the repository and `"secure-http": false`), **Composer** (`composer config repositories.php ...` and `secure-http false`), **auth.json** (`http-basic.localhost:5000 __token__ YOUR_TOKEN`) and **Upload** (`composer archive` and `curl ... -T package.zip .../packages/upload/vendor/package/1.0.0`) |
| CP3 | `admin` | In the container, in the package folder, `composer archive --format=zip --file=package`; upload it with `curl --fail-with-body -u __token__:<token> -T package.zip` to `http://pagetto:8080/feeds/php/composer/packages/upload/contoso/widgets/1.0.0`; again; to `.../1.1.0`; to `.../dev-main`; to `.../2.0.0` with the token of `carol` | 201 for 1.0.0, 1.1.0 and dev-main; the second upload of 1.0.0: `Version 1.0.0 of package contoso/widgets already exists.` (409); `carol`: `You do not have permission to upload packages to this feed.` (403) |
| CP4 | none | In a consumer project with the feed and the credentials: `composer require contoso/widgets:^1.0`, then `php -r 'require "vendor/autoload.php"; var_dump(class_exists("Contoso\\Widgets\\Widget"));'`; then `composer require contoso/widgets:dev-main` | Composer installs contoso/widgets 1.1.0 from the feed and psr/log from Packagist, the class exists, and `composer.lock` has a dist URL on the feed with a 40-character reference and shasum. Then it installs `dev-main` from the feed. Without the credentials, Composer stops with `URL required authentication (HTTP 401)` |
| CP5 | `carol` | Open php (`/feeds/php/`), then contoso/widgets | "PHP feed" above the feed name, the version shown as `1.1.0` (without a leading v), and **Install** copies `composer require contoso/widgets:1.1.0`. The package page shows 1.1.0 with a **latest** badge, `PHP >=8.1`, the tabs **Composer** and **composer.json**, the readme, `psr/log ^3.0` under **Dependencies**, three versions with dev-main marked as a prerelease, MIT, **Package type** library, **Source repository**, the keyword `widgets` and **Download zip** |
| CP6 | none | `curl -i http://localhost:5000/feeds/php/composer/packages.json`; the same with `-u __token__:<token>`, then for `p2/contoso/widgets.json`, `p2/contoso/widgets~dev.json` and `p2/contoso/unknown.json`; then with the token for `/feeds/php/v3/index.json` and `/feeds/internal/composer/packages.json` | Without the token: 401 with `WWW-Authenticate: Basic realm="PaGetto"`. With it: `packages.json` has `"metadata-url":"http://localhost:5000/feeds/php/composer/p2/%package%.json"`, the tagged file lists 1.1.0 and 1.0.0, the `~dev` file lists dev-main, and the unknown package is 404. The NuGet API on the PHP feed and the repository on a NuGet feed: 404 |
| CP7 | `admin` | Add the mirror `https://repo.packagist.org` to php. In a new consumer project, also run `composer config repo.packagist false`, then `composer require monolog/monolog:^3.0 contoso/widgets:^1.0`; delete `vendor` and the Composer cache and run `composer install`; reload php; on the package page of contoso/widgets **Unlist 1.0.0**, **Relist 1.0.0** and **Delete 1.0.0**; delete the feed afterwards | Composer gets monolog/monolog and psr/log through the feed, and their dist URLs in `composer.lock` are on the feed; `composer install` works from the lock. Both are in the package list, and their versions carry a **mirror** label. While 1.0.0 is unlisted, `p2/contoso/widgets.json` leaves it out. The buttons ask for confirmation and work. The audit log has `package_upload_succeeded`, `package_unlist_succeeded`, `package_relist_succeeded` and `package_delete_succeeded` rows for feed `php`. Do `down -v` afterwards |

## CocoaPods feeds

The data set has no CocoaPods feed, so PO1 creates one and the later checks use it. They need internet access for cdn.cocoapods.org and GitHub, and a personal access token of `admin` from **My Tokens** in place of `<token>`. CocoaPods runs in a Ruby container on the test environment's network, where the server is `pagetto:8080`. `pod install` works on Linux for resolving and downloading pods, but not as root and not without `rsync`; it needs an Xcode project, which the `xcodeproj` gem that comes with CocoaPods can create:

```bash
docker run --rm -it --network pagetto-test_default -v "$PWD:/work" -w /work ruby:3.4 sh -c "apt-get update && apt-get install -y unzip rsync && gem install cocoapods && useradd -m dev && su dev"
ruby -rxcodeproj -e 'p = Xcodeproj::Project.new("App.xcodeproj"); p.new_target(:application, "App", :ios, "13.0"); p.save'
printf 'machine pagetto\n  login __token__\n  password <token>\n' > ~/.netrc && chmod 600 ~/.netrc
```

The test pod is `ContosoWidgets` 1.0.0: a `ContosoWidgets.podspec.json` (`"name": "ContosoWidgets"`, `"version": "1.0.0"`, `"summary": "Contoso widgets"`, `"license": {"type": "MIT"}`, `"homepage": "https://example.com/widgets"`, `"authors": {"Contoso": "dev@example.com"}`, `"platforms": {"ios": "13.0"}`, `"swift_versions": ["5.0"]`, `"source_files": "Sources/**/*.swift"`, `"dependencies": {"Alamofire": ["~> 5.9"]}`) and a `ContosoWidgets-1.0.0.zip` with `Sources/Widget.swift`. A consumer `Podfile` has `source 'http://pagetto:8080/feeds/ios/cocoapods/'`, `source 'https://cdn.cocoapods.org/'`, `platform :ios, '13.0'` and `target 'App' do pod 'ContosoWidgets', '1.0.0' end`.

| # | Account | Steps | Expected |
|---|---|---|---|
| PO1 | `admin` | Admin > Feeds > **New feed**: slug `ios`, type **CocoaPods**; open its **Settings**; give the group of `carol` Pull on it | The feed shows a **CocoaPods** badge and its copy button copies `http://localhost:5000/feeds/ios/cocoapods/`. Settings have a Retention section whose limits say "Unlimited" rather than the global NuGet values, and no Deletion behavior setting, the mirror URL placeholder is `https://cdn.cocoapods.org`, and the mirror text speaks of CDN spec repositories and sources that stay where the podspecs point |
| PO2 | `carol` | ios > **Connect** | Spec repository URL `http://localhost:5000/feeds/ios/cocoapods/` with a copy button, an "Authentication required" note about `~/.netrc` that links to My Tokens, a "This feed has no mirror" note, and the tabs **Podfile** (the feed's source and `https://cdn.cocoapods.org/`), **.netrc** (`machine localhost`, `login __token__`, `password YOUR_TOKEN`, `chmod 600`), **pod repo** (`pod repo add-cdn ios ...`) and **Publish** (`pod ipc spec` and `curl ... -F podspec=@... -F source=@... .../api/v1/pods`) |
| PO3 | `admin` | In the container: `curl --fail-with-body --netrc -F podspec=@ContosoWidgets.podspec.json -F source=@ContosoWidgets-1.0.0.zip http://pagetto:8080/feeds/ios/cocoapods/api/v1/pods`; again; then without `source` with a podspec of version 1.1.0 that has no `source`; then 1.1.0 with `"source": {"git": "https://example.com/widgets.git", "tag": "1.1.0"}`; then 2.0.0 with the token of `carol` | `Published ContosoWidgets 1.0.0.` (201); the second: `Version 1.0.0 of pod ContosoWidgets already exists.` (409); the podspec without a source: `The podspec has no source.` (400); 1.1.0: 201; `carol`: `You do not have permission to publish pods to this feed.` (403) |
| PO4 | none | In the consumer folder, `pod install`; then `cat Pods/ContosoWidgets/Sources/Widget.swift` and the `SPEC REPOS` of `Podfile.lock`; then `pod repo update pagetto-feeds-ios-cocoapods --verbose`, `pod spec cat ContosoWidgets --version=1.1.0` and `pod search ContosoWidgets --simple`; then remove the `password` line from `~/.netrc`, move `~/.cocoapods` away and run `pod install`; then put the password back and `chmod 644 ~/.netrc` | CocoaPods adds the spec repo `pagetto-feeds-ios-cocoapods` with the feed's CDN URL, installs ContosoWidgets 1.0.0 from the feed's source archive and Alamofire from the trunk, the Swift file is there, and the lock lists ContosoWidgets under the feed's URL. `pod repo update` reports the unchanged files as not modified, `pod spec cat` prints the 1.1.0 podspec, and `pod search` finds the pod. Without the password: `Unable to add a source with url ...`. With a readable `.netrc`: `Permission bits for '.../.netrc' should be 0600` |
| PO5 | `carol` | Open ios (`/feeds/ios/`), then ContosoWidgets | "CocoaPods feed" above the feed name, the version shown as `v1.1.0`, and **Install** copies `pod 'ContosoWidgets', '1.1.0'`. The package page shows 1.1.0 with a **latest** badge, `iOS 13.0` and `Swift 5.0`, the **Podfile** tab with the feed's source, `Alamofire ~> 5.9` under **Dependencies**, two versions where 1.0.0 has a **Download** link, MIT and **Homepage**. On 1.0.0, **Download source** downloads the zip |
| PO6 | none | `curl -i http://localhost:5000/feeds/ios/cocoapods/CocoaPods-version.yml`; the same with `-u __token__:<token>`, then for `all_pods.txt`, the index file of ContosoWidgets' shard (the first three hex digits of `printf ContosoWidgets \| md5sum`, as `all_pods_versions_a_b_c.txt`) and `Specs/a/b/c/ContosoWidgets/1.0.0/ContosoWidgets.podspec.json`; then with the token for `/feeds/ios/v3/index.json` and `/feeds/internal/cocoapods/CocoaPods-version.yml` | Without the token: 401 with `WWW-Authenticate: Basic realm="PaGetto"`. With it: the version file has `last: 1.8.0` and the prefix lengths, `all_pods.txt` lists ContosoWidgets, the index file has `ContosoWidgets/1.0.0/1.1.0`, and the podspec's `source` is an `http` URL on the feed with `type` and `sha256`. Each answer has an `ETag`. The NuGet API on the CocoaPods feed and the spec repository on a NuGet feed: 404 |
| PO7 | `admin` | Add the mirror `https://cdn.cocoapods.org` to ios. In the consumer, remove the `cdn.cocoapods.org` source from the Podfile and add `pod 'SwiftyJSON', '~> 5.0'`, then `pod install --repo-update`; move `~/.cocoapods` and `Pods` away and run `pod install` again; reload ios; on the package page of ContosoWidgets **Unlist 1.1.0**, then `pod repo update` and check the index file; **Relist 1.1.0** and **Delete 1.1.0**; delete the feed afterwards | CocoaPods gets the podspecs of Alamofire and SwiftyJSON through the feed and their sources from GitHub, and the lock lists them under the feed's URL; the second install works from the lock. Alamofire and SwiftyJSON are in the package list with all their versions, and its versions carry a **mirror** label. While 1.1.0 is unlisted, the index file leaves it out. The buttons ask for confirmation and work. The audit log has `package_upload_succeeded`, `package_unlist_succeeded`, `package_relist_succeeded` and `package_delete_succeeded` rows for feed `ios`. Do `down -v` afterwards |

## Swift feeds

The data set has no Swift feed, so SW1 creates one and the later checks use it. They need a personal access token of `admin` from **My Tokens** in place of `<token>`, and one of `carol` in place of `<carol-token>`. Swift Package Manager only signs in to a registry over HTTPS, so a Caddy container with its own CA serves the test environment as `https://pagetto.test`, and Swift runs in a container on the same network that trusts Caddy's root certificate:

```bash
printf 'pagetto.test {
  tls internal
  reverse_proxy pagetto:8080
}
' > Caddyfile
docker run -d --name swift-caddy --network pagetto-test_default --network-alias pagetto.test -v "$PWD/Caddyfile:/etc/caddy/Caddyfile" caddy:2
docker exec swift-caddy cat /data/caddy/pki/authorities/local/root.crt > caddy-root.crt
docker run --rm -it --network pagetto-test_default -v "$PWD:/work" -w /work swift:6.1 sh -c "apt-get update && apt-get install -y zip && cp caddy-root.crt /usr/local/share/ca-certificates/ && update-ca-certificates && bash"
```

The test package is `contoso.Widgets` 1.0.0: a folder `Widgets` with a `Package.swift` (`// swift-tools-version:5.9`, a library product and target `Widgets`), a `Package@swift-6.swift` (`// swift-tools-version:6.0`, the same with `swiftSettings: [.define("SWIFT6")]`), `Sources/Widgets/Widgets.swift` (`public struct Widgets { public init() {} }`), a `README.md` and a `package-metadata.json` with a `description`, a `licenseURL`, `"repositoryURLs": ["https://github.com/contoso/widgets"]` and `"author": {"name": "Contoso"}`. A consumer folder `App` has a `Package.swift` with `.package(id: "contoso.Widgets", from: "1.0.0")`, an executable target `App` that depends on `.product(name: "Widgets", package: "contoso.Widgets")`, and `Sources/App/main.swift` that imports it.

| # | Account | Steps | Expected |
|---|---|---|---|
| SW1 | `admin` | Admin > Feeds > **New feed**: slug `swift`, type **Swift**; open its **Settings**; give the group of `carol` Pull on it | The feed shows a **Swift** badge and its copy button copies `http://localhost:5000/feeds/swift/swift`. Settings have a Retention section whose limits say "Unlimited" rather than the global NuGet values, and no Deletion behavior setting, the mirror URL placeholder is `https://registry.example.com`, and the mirror text speaks of Swift package registries and the mirror's checksum |
| SW2 | `carol` | swift > **Connect** | Registry URL `http://localhost:5000/feeds/swift/swift` with a copy button, an "Authentication required" note that links to My Tokens, a "This server is not served over HTTPS" note, and the tabs **Set up** (`swift package-registry set --global --allow-insecure-http ...` and `swift package-registry login .../swift/login --token YOUR_TOKEN`), **Package.swift** (`.package(id:` and `.product(`) and **Publish** (`swift package-registry publish mona.LinkedList 1.0.0 --url ... --allow-insecure-http`) |
| SW3 | `admin` | In the container, in `Widgets`: `swift package-registry login https://pagetto.test/feeds/swift/swift/login --token <token> --no-confirm`, then `swift package-registry publish contoso.Widgets 1.0.0 --url https://pagetto.test/feeds/swift/swift`, again, then with a new `HOME` and `<carol-token>` as 1.1.0 | `Login successful.`; `contoso.Widgets version 1.0.0 was successfully published to ...`; the second: `server error 409: Version 1.0.0 of package contoso.Widgets already exists.`; `carol`: `server error 403: You do not have permission to publish packages to this feed.` |
| SW4 | none | In the container with a new `HOME`, in `App`: `swift package-registry set https://pagetto.test/feeds/swift/swift` and `swift package resolve`; then `swift package-registry login https://pagetto.test/feeds/swift/swift/login --token <carol-token> --no-confirm`, `swift package resolve` and `swift run App` | Without the token, resolve fails at once (it doesn't hang) with `server error 401: Authentication is required. Sign in with: swift package-registry login ...`. With it, `Package.resolved` pins contoso.Widgets 1.0.0 with `"kind" : "registry"`, the build uses `Package@swift-6.swift` (the server log has a request for `Package.swift?swift-version=6.0.0`), and the app runs. Resolve warns that the source archive is not signed |
| SW5 | `carol` | Open swift (`/feeds/swift/`), then contoso.Widgets | "Swift feed" above the feed name, the version shown as `v1.0.0`, and **Install** copies `.package(id: "contoso.Widgets", from: "1.0.0")`. The package page shows the description, `Swift tools 5.9`, the readme, the **Package.swift** and **Registry** tabs, Author Contoso, **Source repository** and **License** links, and **Download zip**, which downloads the source archive |
| SW6 | none | `curl -i http://localhost:5000/feeds/swift/swift/contoso/Widgets`; the same with `-H "Authorization: Bearer <token>"`, then for `.../contoso/Widgets/1.0.0`, `.../1.0.0/Package.swift`, `.../1.0.0.zip`, `.../swift/identifiers?url=git@github.com:contoso/widgets.git` and `.../contoso/Unknown`; then with the token for `/feeds/swift/v3/index.json` and `/feeds/internal/swift/contoso/Widgets` | Without the token: 401 with a problem detail and no `WWW-Authenticate` header. With it, every answer has `Content-Version: 1`: the release list has 1.0.0 with its URL, the release has `"id":"contoso.Widgets"`, the `source-archive` checksum and the metadata, `Package.swift` is `text/x-swift` with a `Link` to `?swift-version=6`, the zip has a `Digest`, the lookup answers `{"identifiers":["contoso.Widgets"]}`, and the unknown package is 404 with a problem detail. The NuGet API on the Swift feed and the registry on a NuGet feed: 404 |
| SW7 | `admin` | Create a second Swift feed `swift-proxy` with the mirror `http://pagetto:8080/feeds/swift/swift`, authentication **Bearer** with `<token>`; give the group of `carol` Pull on it. In the consumer with a new `HOME`, set the registry to `https://pagetto.test/feeds/swift-proxy/swift`, log in with `<carol-token>`, `swift package resolve` and `swift run App`; reload swift-proxy; on the package page of contoso.Widgets in swift **Unlist 1.0.0**, check the release list, **Relist 1.0.0** and **Delete 1.0.0**; delete the feeds afterwards | Swift Package Manager gets contoso.Widgets 1.0.0 through swift-proxy, which stores it: it is in the package list of swift-proxy with a **mirror** label on its version. While 1.0.0 is unlisted, the release list of swift leaves it out and the zip still downloads. The buttons ask for confirmation and work. The audit log has `package_upload_succeeded`, `package_unlist_succeeded`, `package_relist_succeeded` and `package_delete_succeeded` rows for feed `swift`. Do `down -v` and `docker rm -f swift-caddy` afterwards |

## R feeds

The data set has no R feed, so CR1 creates one and the later checks use it. They need a personal access token of `admin` from **My Tokens** in place of `<token>`, and one of `carol` in place of `<carol-token>`. R runs in a container on the test environment's network, with the token in `PAGETTO_TOKEN`:

```bash
docker run --rm -it --network pagetto-test_default -e PAGETTO_TOKEN=<carol-token> -v "$PWD:/work" -w /work rocker/r-ver:4.4.2 bash
```

The test package is `ContosoHello` 1.0.0: a folder `ContosoHello` with a `DESCRIPTION` (`Package: ContosoHello`, `Title: Says Hello for the PaGetto Tests`, `Version: 1.0.0`, `Authors@R` with a maintainer, `License: MIT`, `Imports: jsonlite`, `URL` and `BugReports` on github.com), a `NAMESPACE` with `export(hello)`, `R/hello.R` (`hello <- function() jsonlite::toJSON(list(greeting = "Hello"), auto_unbox = TRUE)`) and a `README.md`. `R CMD build ContosoHello` writes `ContosoHello_1.0.0.tar.gz`; build 1.1.0 the same way after changing the version.

| # | Account | Steps | Expected |
|---|---|---|---|
| CR1 | `admin` | Admin > Feeds > **New feed**: slug `rlang`, type **R**; open its **Settings**; give the group of `carol` Pull on it | The feed shows an **R** badge and its copy button copies `http://localhost:5000/feeds/rlang/cran`. Settings have a Retention section whose limits say "Unlimited" rather than the global NuGet values, and no Deletion behavior setting, the overwrite help says that only Allow all replaces a version, the mirror URL placeholder is `https://cloud.r-project.org`, and the mirror text speaks of CRAN-like repositories and the mirror's MD5 |
| CR2 | `carol` | rlang > **Connect** | Repository URL `http://localhost:5000/feeds/rlang/cran` with a copy button, an "Authentication required" note that links to My Tokens, a "Source packages only" note, a "This feed has no mirror" note, and the tabs **.Rprofile** (`options(repos = c(` with `sprintf("http://__token__:%s@localhost:5000/feeds/rlang/cran", Sys.getenv("PAGETTO_TOKEN"))` and CRAN), **R** (`install.packages`), **renv** (`renv::install`, and `renv.config.repos.override`) and **Upload** (`R CMD build` and `curl --fail-with-body -u __token__:YOUR_TOKEN -T mypackage_1.0.0.tar.gz .../src/contrib/`) |
| CR3 | none | `curl --fail-with-body -u __token__:<token> -T ContosoHello_1.0.0.tar.gz http://localhost:5000/feeds/rlang/cran/src/contrib/`, again, then with `<carol-token>` and 1.1.0 | 201; the second: 409 `Version 1.0.0 of package ContosoHello already exists.`; `carol`: 403 `You do not have permission to upload packages to this feed.` |
| CR4 | none | In the container, `Rscript -e 'available.packages(repos = "http://pagetto:8080/feeds/rlang/cran")'`; then with `options(repos = c(rlang = sprintf("http://__token__:%s@pagetto:8080/feeds/rlang/cran", Sys.getenv("PAGETTO_TOKEN")), CRAN = "https://cloud.r-project.org"))`: `install.packages("ContosoHello")`, `ContosoHello::hello()`; upload 1.1.0 as `admin`, then in a project `renv::init(bare = TRUE)`, `renv::install("ContosoHello@1.0.0")` and `renv::snapshot()`; in a new container with the same options and `options(renv.config.repos.override = getOption("repos"))`, `renv::restore()` in that project | Without the token: a warning that `PACKAGES.rds` answered 401. With it: jsonlite comes from CRAN and ContosoHello from the feed, and `hello()` answers `{"greeting":"Hello"}`. renv installs 1.0.0 from `src/contrib/Archive/ContosoHello/` (in the server log). `renv.lock` lists the repository `http://pagetto:8080/feeds/rlang/cran` without the token, and the restore installs ContosoHello 1.0.0 |
| CR5 | `carol` | Open rlang (`/feeds/rlang/`), then ContosoHello | "R feed" above the feed name, ContosoHello with its title and `v1.1.0`, and **Install** copies `install.packages("ContosoHello", repos = "http://localhost:5000/feeds/rlang/cran")`. The package page shows the title, the readme, the **R** and **renv** tabs, jsonlite under **Dependencies**, the maintainer, License MIT, **Homepage** and **Issue tracker** links and **Download tarball**, which downloads `ContosoHello_1.1.0.tar.gz` |
| CR6 | none | `curl -i http://localhost:5000/feeds/rlang/cran/src/contrib/PACKAGES.gz`; then with `-u __token__:<token>` for `src/contrib/PACKAGES`, `src/contrib/PACKAGES.rds`, `src/contrib/Archive/ContosoHello/`, `src/contrib/Archive/ContosoHello/ContosoHello_1.0.0.tar.gz`, `bin/windows/contrib/4.4/PACKAGES` and `src/contrib/Unknown_1.0.tar.gz`; then with the token for `/feeds/rlang/v3/index.json` and `/feeds/internal/cran/src/contrib/PACKAGES` | Without the token: 401 with `WWW-Authenticate: Basic realm="PaGetto"`. With it: `PACKAGES` has one ContosoHello entry, version 1.1.0, with `Imports`, `License`, `MD5sum` and `NeedsCompilation`; `PACKAGES.rds` is 404; the archive lists `ContosoHello_1.0.0.tar.gz` only, which downloads; the binary index is empty (200); the unknown package is 404. The NuGet API on the R feed and the repository on a NuGet feed: 404 |
| CR7 | `admin` | Create a second R feed `cranproxy` with the mirror `https://cloud.r-project.org`; give the group of `carol` Pull on it. In the container, with `options(repos = c(proxy = sprintf("http://__token__:%s@pagetto:8080/feeds/cranproxy/cran", Sys.getenv("PAGETTO_TOKEN"))))`: `nrow(available.packages())`, `install.packages("praise")`, and in a project `renv::install("glue@1.6.2")`; reload cranproxy; on the package page of ContosoHello in rlang **Unlist 1.1.0**, check `PACKAGES`, **Relist 1.1.0** and **Delete 1.1.0**; delete the feeds afterwards | The index has more than 20,000 packages. praise and glue 1.6.2 (from CRAN's archive) install through cranproxy, which stores them: they are in its package list with a **mirror** label on their versions, and the server log has `Mirrored package glue 1.6.2 from https://cloud.r-project.org/`. While 1.1.0 is unlisted, `PACKAGES` of rlang has 1.0.0 and the tarball of 1.1.0 still downloads. The buttons ask for confirmation and work. The audit log has `package_upload_succeeded`, `package_unlist_succeeded`, `package_relist_succeeded` and `package_delete_succeeded` rows for feed `rlang`. Do `down -v` afterwards |

## Ivy feeds

The data set has no Ivy feed, so IV1 creates one and the later checks use it. They need a personal access token of `admin` from **My Tokens** in place of `<token>`, and one of `carol` in place of `<carol-token>`. sbt runs in a container on the test environment's network, with the credentials file of the Connect page (host `pagetto`) mounted as `/root/.sbt/.pagetto-credentials`:

```bash
docker run --rm -it --network pagetto-test_default -v "$PWD:/work" -w /work -v "$PWD/admin.credentials:/root/.sbt/.pagetto-credentials:ro" sbtscala/scala-sbt:eclipse-temurin-17.0.21_9_1.13.1_3.9.0 bash
```

The test plugin is `com.contoso:sbt-hello` 0.1.0: a `build.sbt` with `sbtPlugin := true`, `organization`, `name`, `version`, `description` and `homepage`, the **Publish** lines of Connect (with `.withAllowInsecureProtocol(true)` on the resolver, since the server is plain HTTP, and the URL `http://pagetto:8080/feeds/sbt/ivy`), and an `AutoPlugin` with a task `hello` that logs `Hello from sbt-hello`.

| # | Account | Steps | Expected |
|---|---|---|---|
| IV1 | `admin` | Admin > Feeds > **New feed**: slug `sbt`, type **Ivy**; open its **Settings**; give the group of `carol` Pull on it | The feed shows an **Ivy** badge and its copy button copies `http://localhost:5000/feeds/sbt/ivy`. Settings have a Retention section whose limits say "Unlimited" rather than the global NuGet values, and no Deletion behavior setting, the mirror URL placeholder is `https://repo.scala-sbt.org/scalasbt/sbt-plugin-releases`, the overwrite policy says "Files of SNAPSHOT versions are always replaced." and the max package size "Applies to each file of a version." |
| IV2 | `carol` | sbt > **Connect** | Repository URL `http://localhost:5000/feeds/sbt/ivy` with a copy button, an "Authentication required" note that links to My Tokens, a "This feed has no mirror" note, and the tabs **sbt**, **sbt plugins** (with `addSbtPlugin`) and **Publish** (`publishMavenStyle := false`), each with `Resolver.ivyStylePatterns` and the credentials file with `realm=PaGetto` and `host=localhost` |
| IV3 | `admin` | In the container, `sbt publish` of the test plugin; then `set version := "0.2.0"` and `publish`; then 0.1.0 again with `set publishConfiguration := publishConfiguration.value.withOverwrite(true)`; then 0.3.0 with the credentials of `carol` | `published ivy to http://pagetto:8080/feeds/sbt/ivy/com.contoso/sbt-hello/scala_2.12/sbt_1.0/0.1.0/ivys/ivy.xml`, after the JAR, sources and docs; the same for 0.2.0. Publishing 0.1.0 again: 409. `carol`: 403 |
| IV4 | none | In a second project with the **sbt plugins** lines and `addSbtPlugin("com.contoso" % "sbt-hello" % "0.+")` in `project/plugins.sbt`, with the credentials of `carol`, run `sbt hello`; then without the credentials file | `Hello from sbt-hello` from 0.2.0. Without credentials the resolution fails with `unauthorized: http://pagetto:8080/feeds/sbt/ivy/com.contoso/sbt-hello/scala_2.12/sbt_1.0/ (PaGetto)` |
| IV5 | `carol` | Open sbt (`/feeds/sbt/`), then com.contoso:sbt-hello:scala_2.12:sbt_1.0 | "Ivy feed" above the feed name, the module with its description and `v0.2.0`, and **Install** copies `addSbtPlugin("com.contoso" % "sbt-hello" % "0.2.0")`. The module page shows the chips **sbt 1.0** and **Scala 2.12**, the **sbt plugin** tab with the resolver and `addSbtPlugin`, the dependencies of the `ivy.xml`, the four files (`docs/…`, `ivys/ivy.xml`, `jars/sbt-hello.jar`, `srcs/…`) with SHA-1 under **Files** (each name downloads the file), Status `release`, a **Homepage** link and both revisions under **Versions** |
| IV6 | none | `curl -i http://localhost:5000/feeds/sbt/ivy/com.contoso/sbt-hello/scala_2.12/sbt_1.0/`; then with `-u carol:<carol-token>` for the same, for `…/0.2.0/jars/sbt-hello.jar.sha1`, for `/feeds/sbt/v3/index.json` and for `/feeds/internal/ivy/com.contoso/sbt-hello/scala_2.12/sbt_1.0/` | Without credentials: 401 with `WWW-Authenticate: Basic realm="PaGetto"`. With them: an HTML listing with links `0.1.0/` and `0.2.0/`, and a 40-character checksum. The NuGet API on the Ivy feed and the Ivy repository on a NuGet feed: 404 |
| IV7 | `admin` | Create a second Ivy feed `sbtproxy` with the mirror `https://repo.scala-sbt.org/scalasbt/sbt-plugin-releases`; give the group of `carol` Pull on it. In a project with `addSbtPlugin("org.foundweekends" % "sbt-bintray" % "0.6.1")`, a `~/.sbt/repositories` with `local`, the feed (`http://pagetto:8080/feeds/sbtproxy/ivy/` with the Ivy pattern of the docs and `allowInsecureProtocol`) and `maven-central`, run `sbt -Dsbt.override.build.repos=true 'show bintrayRepository'` with the credentials of `carol`; reload sbtproxy; on the module page of sbt-hello in sbt **Unlist 0.2.0**, check the listing, **Relist 0.2.0** and **Delete 0.2.0**; delete the feeds afterwards | sbt shows `maven`. sbt-bintray and sbt-bintray-core are in the package list of sbtproxy with a **mirror** label on their versions, and the server log has `Mirrored Ivy file org.foundweekends/sbt-bintray/scala_2.12/sbt_1.0/0.6.1/jars/sbt-bintray.jar from https://repo.scala-sbt.org/scalasbt/sbt-plugin-releases`. While 0.2.0 is unlisted the listing has 0.1.0 only and the JAR of 0.2.0 still downloads. The buttons ask for confirmation and work. The audit log has `package_upload_succeeded`, `package_unlist_succeeded`, `package_relist_succeeded` and `package_delete_succeeded` rows for feed `sbt`. Do `down -v` afterwards |

## VSIX feeds

The data set has no VSIX feed, so VS1 creates one and the later checks use it. They need a personal access token of `alice` from **My Tokens** in place of `<alice-token>`, and one of `carol` in place of `<carol-token>`. The test extension is a VSIX file `HelloTools.vsix` built by the VSIX project of the Visual Studio SDK (or by `VsixPackageBuilder` of the tests), with the identifier `Contoso.HelloTools`, a display name, a description, an icon and a preview image, in versions 1.0 and 1.1.

| # | Account | Steps | Expected |
|---|---|---|---|
| VS1 | `admin` | Admin > Feeds > **New feed**: slug `vs`, type **VSIX**, display name `Visual Studio`; open its **Settings**; give the group of `carol` Pull on it, and the group Package owners Pull, Push and Delete | The feed shows a **VSIX** badge and its copy button copies `http://localhost:5000/feeds/vs/vsix/atom.xml`. Settings have a Retention section whose limits say "Unlimited" rather than the global NuGet values, and no Deletion behavior setting, the mirror URL placeholder is `https://www.vsixgallery.com/feed/`, and the overwrite policy says "Extensions have no prerelease versions, so only Allow all replaces a version." |
| VS2 | `carol` | vs > **Connect** | The gallery URL `http://localhost:5000/feeds/vs/vsix/auth/YOUR_TOKEN/atom.xml` with a copy button and "replace YOUR_TOKEN with a personal access token", a note on adding the gallery under Tools > Options > Environment > Extensions, an "Authentication required" note that explains why the token is in the URL and links to My Tokens, and the tabs **Upload** (curl to `/vsix/upload`), **PowerShell** and **VSIXInstaller**. No console errors |
| VS3 | none | `curl -u __token__:<alice-token> -T HelloTools-1.0.vsix http://localhost:5000/feeds/vs/vsix/upload`, the same for 1.1 and again for 1.1; then 1.0 with `<carol-token>`; then `curl -T HelloTools-1.0.vsix http://localhost:5000/feeds/vs/vsix/auth/<alice-token>/upload`; then a VS Code extension | 201, 201, 409 (`Version 1.1 of extension Contoso.HelloTools already exists.`), 403 for carol, 404 for the token URL, 400 (`is a VS Code extension`) |
| VS4 | none | `curl -i http://localhost:5000/feeds/vs/vsix/atom.xml`; then `curl http://localhost:5000/feeds/vs/vsix/auth/<carol-token>/atom.xml`; follow its `content` and `icon` links; then `/feeds/vs/v3/index.json` with the token URL's credentials, and `/feeds/internal/vsix/auth/<carol-token>/atom.xml` | Without a token: 401 with `WWW-Authenticate: Basic realm="PaGetto"`. Through the token URL: an Atom feed with one entry, `Contoso.HelloTools` 1.1, whose `content` and `icon` links are below `/feeds/vs/vsix/auth/<carol-token>/` and download the VSIX file and a PNG. The NuGet API on the VSIX feed and the gallery on a NuGet feed: 404 |
| VS5 | `carol` | In Visual Studio 2022 or later, add the token URL of VS2 under Tools > Options > Environment > Extensions > Additional Extension Galleries; open Extensions > Manage Extensions and the gallery; install Hello Tools; then upload a version 1.2 as in VS3 and check for updates | The gallery lists Hello Tools 1.1 with its icon, description and preview image, and installs it. After the upload, Visual Studio offers the update to 1.2 |
| VS6 | `carol` | Open vs (`/feeds/vs/`), then Hello Tools | "VSIX feed" above the feed name, Hello Tools with its description and `v1.1`, and **Install** copies the token URL of VS2. The page shows the chip **Visual Studio [17.0, 18.0)**, the tabs **Gallery** (the token URL) and **VSIXInstaller** (`VSIXInstaller.exe Contoso.HelloTools-1.1.vsix`), the dependencies of the manifest, the tags under Topics, Extension ID and Publisher under About, a **Homepage** link, **Download VSIX**, and both versions under **Versions** |
| VS7 | `admin` | On the settings of vs, add the mirror `https://www.vsixgallery.com/feed/`; read the token URL of VS4 again; download the `content` link of an extension of the mirror; reload vs; on the page of Hello Tools **Unlist 1.1**, read the gallery, **Relist 1.1** and **Delete 1.1**; delete the feed afterwards | The gallery has the extensions of vsixgallery.com with Hello Tools, all linked below `/feeds/vs/vsix/auth/<carol-token>/`. The downloaded extension is in the package list with a **mirror** label on its version, and the server log has `Mirrored extension … from https://www.vsixgallery.com/feed/ with result Success`. While 1.1 is unlisted the gallery has 1.0, and the file of 1.1 still downloads. The buttons ask for confirmation and work. The audit log has `package_upload_succeeded`, `package_unlist_succeeded`, `package_relist_succeeded` and `package_delete_succeeded` rows for feed `vs`. Do `down -v` afterwards |

## VS Code feeds

The data set has no VS Code feed, so VC1 creates one and the later checks use it. They need a personal access token of `alice` from **My Tokens** in place of `<alice-token>`, and one of `carol` in place of `<carol-token>`. The test extension is a VSIX file built by `vsce package` (or by `VsCodePackageBuilder` of the tests) with the identifier `contoso.hello-world`, the display name Hello World, a readme and an icon, in versions 1.0.0 and 1.1.0, and a file of 1.1.0 built with `vsce package --target linux-x64`.

| # | Account | Steps | Expected |
|---|---|---|---|
| VC1 | `admin` | Admin > Feeds > **New feed**: slug `code`, type **VS Code**, display name `VS Code`; open its **Settings**; give the group of `carol` Pull on it, and the group Package owners Pull, Push and Delete | The feed shows a **VS Code** badge and its copy button copies `http://localhost:5000/feeds/code/vscode/gallery`. Settings have a Retention section whose limits say "Unlimited" rather than the global NuGet values, and no Deletion behavior setting, and the mirror URL placeholder is `https://open-vsx.org/vscode/gallery` |
| VC2 | `carol` | code > **Connect** | The gallery service URL `http://localhost:5000/feeds/code/vscode/auth/YOUR_TOKEN/gallery` with a copy button and "replace YOUR_TOKEN with a personal access token", a note on the editors that read `extensionsGallery` from `product.json` and on VS Code from Microsoft, an "Authentication required" note that links to My Tokens, and the tabs **product.json** (`serviceUrl`, `itemUrl`, `extensionUrlTemplate`), **code-server** (`EXTENSIONS_GALLERY`), **Upload** (curl to `/vscode/upload`) and **PowerShell**. No console errors |
| VC3 | none | `curl -u __token__:<alice-token> -T hello-world-1.0.0.vsix http://localhost:5000/feeds/code/vscode/upload`, the same for 1.1.0, again for 1.1.0, and for the linux-x64 file of 1.1.0; then 1.0.0 with `<carol-token>`; then `curl -T hello-world-1.0.0.vsix http://localhost:5000/feeds/code/vscode/auth/<alice-token>/upload`; then a Visual Studio extension (`HelloTools.vsix`) | 201, 201, 409 (`Version 1.1.0 of extension contoso.hello-world already exists.`), 201, 403 for carol, 404 for the token URL, 400 (`does not target Microsoft.VisualStudio.Code`) |
| VC4 | none | `curl -i -X POST -H "Content-Type: application/json" -d '{"filters":[{"criteria":[{"filterType":7,"value":"contoso.hello-world"}]}],"flags":1}' http://localhost:5000/feeds/code/vscode/gallery/extensionquery`; the same through `/feeds/code/vscode/auth/<carol-token>/gallery/extensionquery`; download its `assetUri` with `/Microsoft.VisualStudio.Services.VSIXPackage?targetPlatform=linux-x64` appended; then the same query on `/feeds/internal/vscode/auth/<carol-token>/gallery/extensionquery` | Without a token: 401 with `WWW-Authenticate: Basic realm="PaGetto"`. Through the token URL: one extension with the versions 1.1.0 for linux-x64 and universal and 1.0.0, whose `assetUri` is below `/feeds/code/vscode/auth/<carol-token>/asset/`, and the download is the linux-x64 file. The gallery on a NuGet feed: 404 |
| VC5 | `carol` | In code-server (`docker run` of `codercom/code-server` on the same network, with `EXTENSIONS_GALLERY` from the code-server tab of VC2 and the server's host name in place of localhost), open the Extensions view and search `hello`; install Hello World; then upload a version 1.2.0 as in VC3 and check for updates | The search lists Hello World with its icon and description, its page shows the readme and "Private Extension", and it installs (the linux-x64 file on Linux) after the prompt to trust the publisher. After the upload, code-server offers the update to 1.2.0 under Available Updates and updates it |
| VC6 | `carol` | Open code (`/feeds/code/`), then Hello World | "VS Code feed" above the feed name, Hello World with its description and `v1.1.0`, and **Install** copies `code --install-extension contoso.hello-world`. The page shows the readme, the chip **VS Code ^1.90.0** (the engine of the test extension), the tabs **CLI** (`code --install-extension contoso.hello-world@1.1.0`) and **VSIX**, the dependencies, the tags without the `__ext_` ones under Topics, Extension ID and Platforms (`linux-x64, universal`) under About, a **Source repository** link, **Download VSIX**, and both versions under **Versions** |
| VC7 | `admin` | On the settings of code, add the mirror `https://open-vsx.org/vscode/gallery`; in code-server search `rust-analyzer` and install it; reload code; on the page of Hello World **Unlist 1.1.0**, query it as in VC4, **Relist 1.1.0** and **Delete 1.1.0**; delete the feed afterwards | code-server lists rust-analyzer from Open VSX with its Install button enabled, and installs its file for its platform. rust-analyzer is in the package list of code with a **mirror** label on its version, and the server log has `Mirrored extension rust-lang.rust-analyzer … from https://open-vsx.org/vscode/gallery`. While 1.1.0 is unlisted the query has 1.0.0 only, and the files of 1.1.0 still download. The buttons ask for confirmation and work. The audit log has `package_upload_succeeded`, `package_unlist_succeeded`, `package_relist_succeeded` and `package_delete_succeeded` rows for feed `code`. Do `down -v` afterwards |

## Small screens

| # | Account | Steps | Expected |
|---|---|---|---|
| Y1 | `admin` | Emulate a 375 x 812 viewport, open Internal, a package page, Connect, Upload, Statistics, Admin > Accounts, Admin > Feeds and a feed's settings | No horizontal page scrolling (wide tables scroll inside their box), rows reflow, the navigation and the user menu are collapsed behind the menu button and open as a panel under the header |

## Security

| # | Account | Steps | Expected |
|---|---|---|---|
| Z1 | `admin` | Create an account named `x'+(document.title='pwned')+'`, disable it, open ⋯ > **Delete account…** and cancel | The dialog shows the username literally; the page title doesn't change |
