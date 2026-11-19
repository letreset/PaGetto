# Quarantine and promotion

A version is servable as soon as it is pushed or mirrored. A feed's quarantine policies hold back the versions that fail them until an administrator has looked at them, and promotion moves a tested version from one feed to another without downloading and pushing it again.

## Quarantine policies

Each feed has two policies, under **Mirrors** in its [settings](feeds.md#feed-settings). They are shown for the feed types that [vulnerability scanning](vulnerabilities.md#what-is-scanned) covers, and both are off by default.

| Setting | Database field | What it quarantines |
|---|---|---|
| Quarantine vulnerable versions | `QuarantineSeverity` | Versions with a known vulnerability of the chosen severity or higher (Critical, High, Moderate or any). New versions wait in quarantine for their first [scan](vulnerabilities.md), which runs within a minute of storing them; a version the scan finds nothing in at that severity is then served. Every later scan applies the policy again, so a version that is served is quarantined when an advisory is published for it. |
| Quarantine malicious packages | `QuarantineMaliciousPackages` | New versions that the [malicious packages data](malicious-packages.md) lists and no administrator allowed, including versions published to the feed (which [blocking](malicious-packages.md#what-is-blocked) never refuses). |

The policies apply to every new version, published or mirrored, and to a version [promoted](#promotion) to the feed.

:::note
With the vulnerability policy on and `VulnerabilityScanning:Enabled` set to false, new versions are not held for a scan, since none would run.
:::

## Quarantined versions

A quarantined version is stored, but the feed behaves as if it didn't have it: its downloads answer `404 Not Found`, and it is left out of version lists, search, indexes and the web UI. A package whose versions are all quarantined is left out too. A client asking for it gets another version, or fails as for a missing one.

**Admin > Quarantine** lists the quarantined versions of every feed with the reason and since when. For each one:

- **Release** serves the version again. The feed's policies are no longer applied to it, so a later scan doesn't quarantine it again.
- **Delete** removes the version and its content. It can then be pushed or mirrored again.

**Quarantine history** opens the audit log filtered to `package_quarantined`.

## Promotion

A user with the pull permission on a feed and the push permission on another feed of the same type can copy a version to it: the **Promote** card on the package page (shown when there is such a feed) opens a form with the target feeds. **Move** also removes the version from the source feed, and needs the delete permission there. Neither feed may be in read-only mode for a move, nor the target for a copy.

The content is copied as stored, so its hashes stay the same: the archive and readme, the files of a version that has several (a Maven POM and JAR, the wheels of a Python version), the manifest and the listed state. Tags such as npm dist-tags are not copied. The copy counts as published to the target feed, even when the source stored it from a mirror. A version the target feed already has is refused, whatever its overwrite policy.

Clients and scripts promote through the API of the source feed:

```bash
curl -u alice:<token> -H "Content-Type: application/json" \
  -d '{"packageId":"Contoso.Utils","version":"1.2.0","targetFeed":"release","move":false}' \
  https://pagetto.example.com/feeds/staging/api/v1/promote
```

`packageId` is the name as the feed stores it (the id of a NuGet package). The answer is `200 OK` with the package, version and feeds, `404` when the source feed has no such version (or it is quarantined), `409 Conflict` when the target has it, `400` for a target of another type, `401` or `403` without the permissions, and `403` for a read-only feed. In the `Legacy` [authentication mode](authentication.md) the API key stands for every permission, and the web UI has no **Promote** card.

## Audit and webhook events

| Event | Actor | When |
|---|---|---|
| `package_quarantined` | `quarantine-policy` | A version is quarantined. The detail has the reason; the webhook's `advisory` field has the advisory of the policy it fails. |
| `package_released` | the administrator, or `quarantine-policy` when the first scan finds nothing | A version leaves quarantine and is served. |
| `package_quarantine_deleted` | the administrator | A quarantined version is deleted. |
| `package_promoted` | the user | A version is copied or moved to the feed of the event. The detail names the source feed. |

Each is stored in the [audit log](configuration.md#audit-log) and sent to the feed's [webhook](feeds.md#webhook).
