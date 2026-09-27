# Run PaGetto on Google Cloud Platform

On Google Cloud, store package metadata in [Cloud SQL](https://cloud.google.com/sql) and packages in [Cloud Storage](https://cloud.google.com/storage/). Run PaGetto itself on [App Engine](#google-app-engine) (flexible environment), [Cloud Run](https://cloud.google.com/run), or [GKE](https://cloud.google.com/kubernetes-engine) with the [Helm chart](kubernetes.md).

For best performance, put Cloud Storage, Cloud SQL and PaGetto in the same region.

## Google Cloud Storage

### Setup

[Create a bucket](https://cloud.google.com/storage/docs/creating-buckets) for the packages. The identity PaGetto runs as needs the **Storage Object Admin** role (`roles/storage.objectAdmin`) on the bucket.

### Configuration

Set the storage type and the bucket name in `appsettings.json`:

```json
{
    ...

    "Storage": {
        "Type": "GoogleCloud",
        "BucketName": "your-gcs-bucket"
    },

    ...
}
```

Or set the `Storage__Type` and `Storage__BucketName` environment variables.

### Credentials

PaGetto uses [Application Default Credentials](https://cloud.google.com/docs/authentication/application-default-credentials), so there is nothing to configure in PaGetto itself:

- On App Engine, Cloud Run and GKE (with [Workload Identity](https://cloud.google.com/kubernetes-engine/docs/how-to/workload-identity)), PaGetto uses the service account attached to the workload.
- Anywhere else, create a service account key, download it as a JSON file and set the [`GOOGLE_APPLICATION_CREDENTIALS`](https://cloud.google.com/docs/authentication/provide-credentials-adc#local-key) environment variable to the path of that file. Treat the file like a password.

## Google Cloud SQL

PaGetto works with Cloud SQL for MySQL, PostgreSQL and SQL Server. The steps below use MySQL.

1. [Create a Cloud SQL for MySQL instance](https://cloud.google.com/sql/docs/mysql/create-instance). The default options work well.
2. Create a database named `pagetto` with the `utf8mb4` character set, and a user for PaGetto.
3. Configure the connection. PaGetto creates and updates its tables on startup, so the user needs permission to change the schema.

On App Engine and Cloud Run, connect through the built-in Cloud SQL connection, which is a Unix socket under `/cloudsql/`:

```json
{
    ...

    "Database": {
        "Type": "MySql",
        "ConnectionString": "Server=/cloudsql/PROJECT:REGION:INSTANCE;Database=pagetto;User Id=pagetto;Password=..."
    },

    ...
}
```

From anywhere else, connect through the [Cloud SQL Auth Proxy](https://cloud.google.com/sql/docs/mysql/sql-proxy), or connect to the instance's IP address with TLS and [client certificates](https://cloud.google.com/sql/docs/mysql/configure-ssl-instance):

```json
{
    ...

    "Database": {
        "Type": "MySql",
        "ConnectionString": "Server=YOURIP;Database=pagetto;User Id=pagetto;Password=...;SslMode=VerifyCA;SslCa=server-ca.pem;SslCert=client-cert.pem;SslKey=client-key.pem"
    },

    ...
}
```

For other database types and more on connection strings, see [Database configuration](../configuration.md#database-configuration).

## Google App Engine

PaGetto can run on the App Engine [flexible environment](https://cloud.google.com/appengine/docs/flexible) as a custom container. Create an `app.yaml` file next to a `Dockerfile` that starts from the published image:

```dockerfile
FROM letreset/pagetto:2
```

In the `app.yaml` template below, make these replacements:

- `PROJECT`: your GCP project, as returned by `gcloud config get-value project`
- `REGION`: the region of your Cloud SQL instance, e.g. `us-central1`
- `INSTANCE`: the name of your Cloud SQL instance
- `PASSWORD`: the password of the `pagetto` database user
- `BUCKETNAME`: the name of the Cloud Storage bucket
- `APIKEY`: a long random value used to push packages, or leave it out and set up [user accounts](../authentication.md)

```yaml
runtime: custom
env: flex

# The settings below keep costs low while testing and are not necessarily
# appropriate for production. See:
# https://cloud.google.com/appengine/docs/flexible/reference/app-yaml
resources:
  cpu: 1
  memory_gb: 1
  disk_size_gb: 10

beta_settings:
  cloud_sql_instances: "PROJECT:REGION:INSTANCE"

liveness_check:
  path: "/livez"

readiness_check:
  path: "/health"

env_variables:
  Database__Type: "MySql"
  Database__ConnectionString: "Server=/cloudsql/PROJECT:REGION:INSTANCE;Database=pagetto;User Id=pagetto;Password=PASSWORD"
  Storage__Type: "GoogleCloud"
  Storage__BucketName: "BUCKETNAME"
  ApiKey: "APIKEY"
```

The container listens on port 8080, which is the port App Engine expects. To publish the application, run `gcloud app deploy`.

Keep the database password and the API key out of `app.yaml` if it is checked in, for example by reading them from [Secret Manager](https://cloud.google.com/secret-manager).

## Run several instances

With the database on Cloud SQL and packages on Cloud Storage, you can run more than one PaGetto instance. [Data Protection keys](../upgrading.md#data-protection-keys) are stored in the bucket as well, so sign-in cookies work on every instance.

## Publish and restore packages

Replace `your-server` with the address of your PaGetto server, for example `PROJECT.REGION_ID.r.appspot.com`.

```shell
dotnet nuget push -s https://your-server/v3/index.json -k <api-key> package.1.0.0.nupkg
```

Restore from `https://your-server/v3/index.json`. Other [feeds](../feeds.md) are at `https://your-server/feeds/{slug}/v3/index.json`, and symbols are at `https://your-server/api/download/symbols`.
