# PaGetto Source Code

These folders contain the core components of PaGetto:

* `PaGetto` - The app's entry point that glues everything together.
* `PaGetto.Core` - PaGetto's core logic and services.
* `PaGetto.Web` - The [NuGet server APIs](https://docs.microsoft.com/en-us/nuget/api/overview) and web UI.
* `PaGetto.Protocol` - Libraries to interact with [NuGet servers' APIs](https://docs.microsoft.com/en-us/nuget/api/overview).

These folders contain database-specific components of PaGetto:

* `PaGetto.Database.MySql` - PaGetto's MySQL database provider.
* `PaGetto.Database.PostgreSql` - PaGetto's PostgreSql database provider.
* `PaGetto.Database.Sqlite` - PaGetto's SQLite database provider.
* `PaGetto.Database.SqlServer` - PaGetto's Microsoft SQL Server database provider.

These folders contain cloud-specific components of PaGetto:

* `PaGetto.Aliyun` - PaGetto's Alibaba Cloud(Aliyun) provider.
* `PaGetto.Aws` - PaGetto's Amazon Web Services provider.
* `PaGetto.Azure` - PaGetto's Azure provider.
* `PaGetto.Gcp` - PaGetto's Google Cloud Platform provider.
* `PaGetto.Tencent` - PaGetto's Tencent Cloud Platform provider.

