# Upgrading

Each [release](https://github.com/letreset/PaGetto/releases) lists its changes in the release notes. If you skip releases, read the notes of every release between your version and the new one. Coming from BaGetter? See [Migrating from BaGetter](migrating-from-bagetter.md).

## Before you upgrade

**Back up the database and the package storage.** PaGetto runs its database migrations automatically on startup (`RunMigrationsAtStartup`, on by default), and there is no downgrade path once they have run.

Pin the image or chart to the version you want, for example `letreset/pagetto:1.0.0`, and upgrade on purpose instead of following `latest`.

## Step by step

1. Back up the database and storage.
2. Change the image tag, the chart version or the zip to the new version. With the zip, keep your `appsettings.json`, the database and the package folder, and replace the rest of the files.
3. Start PaGetto and check the log for migration errors.
4. Browse to the site, and restore and push a package to confirm everything works.

## Data Protection keys

PaGetto keeps its ASP.NET Core Data Protection keys (which protect sign-in cookies and forms) in the configured package storage, at `dataprotection/keyring.xml`. With file system storage in Docker this is inside `/data`. If `/data` isn't a persistent volume, every restart signs everybody out, and several replicas can't share cookies.
