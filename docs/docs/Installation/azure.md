import Tabs from '@theme/Tabs';
import TabItem from '@theme/TabItem';

# Run PaGetto on Azure

On Azure, store package metadata in [Azure SQL Database](https://azure.microsoft.com/products/azure-sql/database/) (or Azure Database for PostgreSQL or MySQL) and packages in [Azure Blob Storage](https://azure.microsoft.com/products/storage/blobs/). Run PaGetto itself on [Azure App Service](#azure-app-service), [Azure Container Apps](https://azure.microsoft.com/products/container-apps/), or [AKS](https://azure.microsoft.com/products/kubernetes-service/) with the [Helm chart](kubernetes.md).

## Configure PaGetto

Set these values in `appsettings.json` or as [environment variables](https://learn.microsoft.com/aspnet/core/fundamentals/configuration/#non-prefixed-environment-variables) (`Storage__Type`, `Database__ConnectionString`, …). For the full list of settings, see [Configuration](../configuration.md).

### Database

Use any SQL database that PaGetto supports: Azure SQL Database (`SqlServer`), Azure Database for PostgreSQL (`PostgreSql`) or Azure Database for MySQL (`MySql`). PaGetto creates and updates its tables on startup. Set the database type and a [connection string](https://learn.microsoft.com/ef/core/miscellaneous/connection-strings):

```json
{
    ...

    "Database": {
        "Type": "SqlServer",
        "ConnectionString": "..."
    },

    ...
}
```

To connect to Azure SQL Database with a managed identity instead of a username and password, add `Authentication=Active Directory Default` to the connection string:

```json
{
    ...

    "Database": {
        "Type": "SqlServer",
        "ConnectionString": "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Authentication=Active Directory Default;Encrypt=True;Connection Timeout=30;"
    },

    ...
}
```

The managed identity must be added as a user in the database before PaGetto can connect. `db_ddladmin` lets PaGetto run its migrations on startup:

```sql
CREATE USER [<identity-name>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [<identity-name>];
ALTER ROLE db_datawriter ADD MEMBER [<identity-name>];
ALTER ROLE db_ddladmin ADD MEMBER [<identity-name>];
```

:::warning Azure Table Storage

PaGetto doesn't support Azure Table Storage as a database (`Database:Type` = `AzureTable`), because it can't store [feeds](../feeds.md), user accounts or permissions. PaGetto refuses to start with it. Use one of the SQL databases above.

:::

### Azure Blob Storage

Create a storage account and a container. Set the storage type to `AzureBlobStorage`, the container name and credentials:

<Tabs groupId="blob-storage">
<TabItem value="managedIdentity" label="Managed identity (recommended)" default>

Set `ConnectionString` to the blob service endpoint and enable `UseAzureDefaultCredential`. PaGetto then authenticates with [`DefaultAzureCredential`](https://learn.microsoft.com/dotnet/azure/sdk/authentication/credential-chains#defaultazurecredential-overview), which uses the managed identity of the App Service, Container App or AKS workload.

```json
{
    ...

    "Storage": {
        "Type": "AzureBlobStorage",
        "Container": "my-container",
        "ConnectionString": "https://<account>.blob.core.windows.net",
        "UseAzureDefaultCredential": true
    },

    ...
}
```

The managed identity must be granted the **Storage Blob Data Contributor** role on the storage account (or the container) before PaGetto can read or write packages.

</TabItem>

<TabItem value="connectionString" label="Connection string">

```json
{
    ...

    "Storage": {
        "Type": "AzureBlobStorage",
        "Container": "my-container",
        "ConnectionString": "AccountName=my-account;AccountKey=abcd1234;..."
    },

    ...
}
```

</TabItem>

<TabItem value="accessKey" label="Access key">

```json
{
    ...

    "Storage": {
        "Type": "AzureBlobStorage",
        "Container": "my-container",
        "AccountName": "my-account",
        "AccessKey": "abcd1234"
    },

    ...
}
```

</TabItem>
</Tabs>

Keep connection strings and keys out of `appsettings.json`: use App Service settings, Key Vault references, environment variables or a [secret file](../configuration.md#load-secrets-from-files).

### Search

PaGetto searches the packages in its database (`Search:Type` = `Database`, the default). Azure AI Search (`AzureSearch`) isn't available yet.

### Sign-in with Microsoft Entra ID

To let users sign in with their Entra ID accounts and manage permissions with app roles, see [Azure Entra ID setup](../authentication.md#azure-entra-id-setup). To send PAT expiry emails through Microsoft 365, see [Microsoft Graph email](../configuration.md#microsoft-graph).

## Azure App Service

Run the [`letreset/pagetto`](https://hub.docker.com/r/letreset/pagetto) image on a Linux App Service:

1. Create a **Web App** with **Publish: Container** and **Operating system: Linux**, and choose the `letreset/pagetto` image from Docker Hub. Pin a version tag, see [image tags](docker.md#image-tags).
2. The container listens on port 8080. Add the app setting `WEBSITES_PORT` = `8080`.
3. Turn on the app's **system-assigned managed identity** and grant it access to the database and the storage account as described above.
4. Add the PaGetto settings as **App settings**, using `__` for nested keys:

   | Name | Value |
   |---|---|
   | `Database__Type` | `SqlServer` |
   | `Database__ConnectionString` | `Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Authentication=Active Directory Default;Encrypt=True;` |
   | `Storage__Type` | `AzureBlobStorage` |
   | `Storage__Container` | `my-container` |
   | `Storage__ConnectionString` | `https://<account>.blob.core.windows.net` |
   | `Storage__UseAzureDefaultCredential` | `true` |
   | `ApiKey` | A long random value, or set up [user accounts](../authentication.md) |

5. Set **Health check** to `/health`, see [Health endpoint](../configuration.md#health-endpoint).

App Service terminates TLS in front of the container and passes the original scheme and client address in `X-Forwarded-*` headers, which PaGetto reads.

## Run several instances

With the database and packages in Azure, you can scale out to more than one instance. [Data Protection keys](../upgrading.md#data-protection-keys) are stored in the blob container as well, so sign-in cookies work on every instance and you don't need session affinity.

## Publish packages

Replace `your-server` with the address of your PaGetto server, for example `pagetto.azurewebsites.net`.

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

- [Visual Studio](https://docs.microsoft.com/nuget/consume-packages/install-use-packages-visual-studio#package-sources)
- [NuGet.config](https://docs.microsoft.com/nuget/reference/nuget-config-file#package-source-sections)

## Symbol server

Use the following symbol location:

`https://your-server/api/download/symbols`

For Visual Studio, see [Configure symbol locations](https://docs.microsoft.com/visualstudio/debugger/specify-symbol-dot-pdb-and-source-files-in-the-visual-studio-debugger#configure-symbol-locations-and-loading-options).
