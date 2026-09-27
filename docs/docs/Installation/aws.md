import Tabs from '@theme/Tabs';
import TabItem from '@theme/TabItem';

# Run PaGetto on AWS

On Amazon Web Services, store package metadata in [Amazon RDS](https://aws.amazon.com/rds) and packages in [Amazon S3](https://aws.amazon.com/s3/) (or any other S3-compatible service). Run PaGetto itself wherever containers run: [Amazon ECS](https://aws.amazon.com/ecs/), [Amazon EKS](https://aws.amazon.com/eks/) with the [Helm chart](kubernetes.md), or an EC2 instance with [Docker](docker.md) or the [release zip](local.md).

## Configure PaGetto

Set these values in `appsettings.json` or as environment variables (`Storage__Type`, `Database__ConnectionString`, …). For the full list of settings, see [Configuration](../configuration.md).

### Amazon S3

Create a bucket and give PaGetto access to it. Set `Region` to the bucket's region:

```json
{
    ...

    "Storage": {
        "Type": "AwsS3",
        "Region": "eu-west-1",
        "Bucket": "my-nuget-packages",
        "Prefix": "pagetto" // optional
    },

    ...
}
```

`Prefix` is optional. When set, every object is stored under that key prefix, so several applications can share a bucket.

#### Credentials

PaGetto needs read, write and delete access to the objects in the bucket (`s3:GetObject`, `s3:PutObject`, `s3:DeleteObject` and `s3:ListBucket`). Pick one way to provide credentials:

<Tabs groupId="aws-credentials">
<TabItem value="default" label="IAM role (recommended)" default>

Leave out all credential settings. The AWS SDK then uses its [default credential chain](https://docs.aws.amazon.com/sdkref/latest/guide/standardized-credentials.html): environment variables, the shared credentials file, an ECS task role, an EKS service account (IRSA or Pod Identity) or an EC2 instance profile.

To use that chain explicitly, set `UseInstanceProfile`:

```json
{
    "Storage": {
        "Type": "AwsS3",
        "Region": "eu-west-1",
        "Bucket": "my-nuget-packages",
        "UseInstanceProfile": true
    }
}
```

</TabItem>
<TabItem value="assumeRole" label="Assume a role">

To access a bucket through another role, for example in another account, set `AssumeRoleArn`. PaGetto uses the default credential chain to assume that role:

```json
{
    "Storage": {
        "Type": "AwsS3",
        "Region": "eu-west-1",
        "Bucket": "my-nuget-packages",
        "AssumeRoleArn": "arn:aws:iam::123456789012:role/pagetto-storage"
    }
}
```

</TabItem>
<TabItem value="accessKey" label="Access key">

Create an IAM user with access to the bucket and an access key for it. `AccessKey` and `SecretKey` must be set together:

```json
{
    "Storage": {
        "Type": "AwsS3",
        "Region": "eu-west-1",
        "Bucket": "my-nuget-packages",
        "AccessKey": "",
        "SecretKey": ""
    }
}
```

Keep the secret key out of `appsettings.json`: use an environment variable or a [secret file](../configuration.md#load-secrets-from-files).

</TabItem>
</Tabs>

#### S3-compatible object storage

You can use any storage service that is compatible with Amazon S3. Set `Endpoint` to the service URL instead of `Region`:

```json
{
    ...

    "Storage": {
        "Type": "AwsS3",
        "Endpoint": "https://eu-central-1.linodeobjects.com",
        "Bucket": "nuget-packages",
        "AccessKey": "",
        "SecretKey": ""
    },

    ...
}
```

Set only one of `Region` and `Endpoint`. PaGetto fails at startup if both are set.

#### Path-style addressing

By default the AWS SDK uses virtual-hosted-style URLs, where the bucket is part of the host name (`https://nuget-packages.example.com/...`). Some S3-compatible services, such as a local [MinIO](https://github.com/minio/minio) server, only support path-style URLs, where the bucket is part of the path (`http://localhost:9000/nuget-packages/...`). Set `ForcePathStyle` to `true` to use path-style addressing. It defaults to `false` and only applies when `Endpoint` is set.

For example, for a local MinIO server:

```json
{
    ...

    "Storage": {
        "Type": "AwsS3",
        "Endpoint": "http://localhost:9000",
        "ForcePathStyle": true,
        "Bucket": "nuget-packages",
        "AccessKey": "minioadmin",
        "SecretKey": "minioadmin"
    },

    ...
}
```

Or with environment variables:

```bash
Storage__Type=AwsS3
Storage__Endpoint=http://localhost:9000
Storage__ForcePathStyle=true
Storage__Bucket=nuget-packages
Storage__AccessKey=minioadmin
Storage__SecretKey=minioadmin
```

#### Known compatible services

So far, PaGetto has been tested with [Linode's Object Storage](https://www.linode.com/docs/products/storage/object-storage/), and [MinIO](https://github.com/minio/minio) has been reported to work with `ForcePathStyle`. If you use PaGetto with another service, let us know so we can list it here.

### Amazon RDS

Create a database for PaGetto. PaGetto creates and updates its tables on startup, so the database user needs permission to change the schema. For more on connection strings, see [Database configuration](../configuration.md#database-configuration).

<Tabs groupId="database-types">
  <TabItem value="postgresql" label="Amazon RDS for PostgreSQL">
To use [PostgreSQL](https://aws.amazon.com/rds/postgresql), set:

```json
{
    ...

    "Database": {
        "Type": "PostgreSql",
        "ConnectionString": "Host=my-db.xxxxxxxx.eu-west-1.rds.amazonaws.com;Database=pagetto;Username=pagetto;Password=..."
    },

    ...
}
```

  </TabItem>
  <TabItem value="mysql" label="Amazon RDS for MySQL">
To use [MySQL](https://aws.amazon.com/rds/mysql), create the database with the `utf8mb4` character set and set:

```json
{
    ...

    "Database": {
        "Type": "MySql",
        "ConnectionString": "Server=my-db.xxxxxxxx.eu-west-1.rds.amazonaws.com;Database=pagetto;User Id=pagetto;Password=..."
    },

    ...
}
```

  </TabItem>
  <TabItem value="sqlserver" label="Amazon RDS for SQL Server">
To use [SQL Server](https://aws.amazon.com/rds/sqlserver), set:

```json
{
    ...

    "Database": {
        "Type": "SqlServer",
        "ConnectionString": "Server=my-db.xxxxxxxx.eu-west-1.rds.amazonaws.com;Database=pagetto;User Id=pagetto;Password=...;Encrypt=True"
    },

    ...
}
```

  </TabItem>
</Tabs>

## Run several instances

With the database on RDS and packages on S3, you can run more than one PaGetto instance behind a load balancer. [Data Protection keys](../upgrading.md#data-protection-keys) are stored in the bucket as well, so sign-in cookies work on every instance. Configure the load balancer's health check to use `/health`, see [Health endpoint](../configuration.md#health-endpoint).

## Publish packages

Replace `your-server` with the address of your PaGetto server.

```shell
dotnet nuget push -s https://your-server/v3/index.json -k <api-key> package.1.0.0.nupkg
```

Publish a [symbol package](https://docs.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg) the same way:

```shell
dotnet nuget push -s https://your-server/v3/index.json -k <api-key> symbol.package.1.0.0.snupkg
```

:::warning

Secure your server by requiring an API key to publish packages. See [Require an API key](../configuration.md#require-an-api-key), or set up [user accounts](../authentication.md).

:::

## Restore packages

Use the following package source:

`https://your-server/v3/index.json`

Other [feeds](../feeds.md) are at `https://your-server/feeds/{slug}/v3/index.json`. Some helpful guides:

- [Visual Studio](https://docs.microsoft.com/en-us/nuget/consume-packages/install-use-packages-visual-studio#package-sources)
- [NuGet.config](https://docs.microsoft.com/en-us/nuget/reference/nuget-config-file#package-source-sections)

## Symbol server

Use the following symbol location:

`https://your-server/api/download/symbols`

For Visual Studio, see [Configure symbol locations](https://docs.microsoft.com/en-us/visualstudio/debugger/specify-symbol-dot-pdb-and-source-files-in-the-visual-studio-debugger#configure-symbol-locations-and-loading-options).
