using System;
using PaGetto.Core;
using PaGetto.Core.Configuration;
using PaGetto.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace PaGetto.Database.Sqlite;

public static class SqliteApplicationExtensions
{
    public static PaGettoApplication AddSqliteDatabase(this PaGettoApplication app)
    {
        app.Services.AddPaGettoDbContextProvider<SqliteContext>("Sqlite");

        return app;
    }

    public static PaGettoApplication AddSqliteDatabase(
        this PaGettoApplication app,
        Action<DatabaseOptions> configure)
    {
        app.AddSqliteDatabase();
        app.Services.Configure(configure);
        return app;
    }
}
