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

### Encrypt the keys with a certificate

By default the key ring is stored unencrypted, and PaGetto logs `No XML encryptor configured` at startup. To encrypt it, give PaGetto a certificate with a private key, either as a PFX file:

```json
{
    "DataProtection": {
        "CertificatePath": "/run/secrets/dataprotection.pfx",
        "CertificatePassword": "..."
    }
}
```

or, on Windows (for example IIS), by the thumbprint of a certificate in the `My` store of the local machine or the current user:

```json
{
    "DataProtection": {
        "CertificateThumbprint": "0123456789ABCDEF0123456789ABCDEF01234567"
    }
}
```

Keep the certificate for as long as the keys it encrypted are in use: without it PaGetto can't read the key ring, and everybody has to sign in again. Keys written before the certificate was set stay readable. Startup fails if both settings are set or the file doesn't exist.
