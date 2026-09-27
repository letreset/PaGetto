using System;
using PaGetto.Core;
using PaGetto.Core.Configuration;
using PaGetto.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace PaGetto.Database.PostgreSql;

public static class PostgreSqlApplicationExtensions
{
    public static PaGettoApplication AddPostgreSqlDatabase(this PaGettoApplication app)
    {
        app.Services.AddPaGettoDbContextProvider<PostgreSqlContext>("PostgreSql");

        return app;
    }

    public static PaGettoApplication AddPostgreSqlDatabase(
        this PaGettoApplication app,
        Action<DatabaseOptions> configure)
    {
        app.AddPostgreSqlDatabase();
        app.Services.Configure(configure);
        return app;
    }
}
