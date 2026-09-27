# PaGetto's PostgreSql Database Provider

This project contains PaGetto's PostgreSql database provider.

## Migrations

Check for pending changes with:
```
dotnet ef migrations has-pending-model-changes --context PostgreSqlContext --startup-project ..\PaGetto\PaGetto.csproj
```

Add a migration with:

```
dotnet ef migrations add MigrationName --context PostgreSqlContext --output-dir Migrations --startup-project ..\PaGetto\PaGetto.csproj
```

Apply the migration to your database with:

```
dotnet ef database update --context PostgreSqlContext
```
