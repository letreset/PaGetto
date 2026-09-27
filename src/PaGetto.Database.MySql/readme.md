# PaGetto's MySQL Database Provider

This project contains PaGetto's MySQL database provider.

## Migrations

Check for pending changes with:
```
dotnet ef migrations has-pending-model-changes --context MySqlContext --startup-project ..\PaGetto\PaGetto.csproj
```

Add a migration with:

```
dotnet ef migrations add MigrationName --context MySqlContext --output-dir Migrations --startup-project ..\PaGetto\PaGetto.csproj
```

Apply the migration to your database with:

```
dotnet ef database update --context MySqlContext
```
