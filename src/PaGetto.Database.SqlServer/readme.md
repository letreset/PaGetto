# PaGetto's SQL Server Database Provider

This project contains PaGetto's Microsoft SQL Server database provider.

## Migrations

Check for pending changes with:
```
dotnet ef migrations has-pending-model-changes --context SqlServerContext --startup-project ..\PaGetto\PaGetto.csproj
```

Add a migration with:

```
dotnet ef migrations add MigrationName --context SqlServerContext --output-dir Migrations --startup-project ..\PaGetto\PaGetto.csproj
```

Apply the migration to your database with:

```
dotnet ef database update --context SqlServerContext
```
