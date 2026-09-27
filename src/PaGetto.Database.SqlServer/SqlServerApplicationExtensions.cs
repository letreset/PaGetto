using System;
using PaGetto.Core;
using PaGetto.Core.Configuration;
using PaGetto.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace PaGetto.Database.SqlServer;

public static class SqlServerApplicationExtensions
{
    public static PaGettoApplication AddSqlServerDatabase(this PaGettoApplication app)
    {
        app.Services.AddPaGettoDbContextProvider<SqlServerContext>("SqlServer");

        return app;
    }

    public static PaGettoApplication AddSqlServerDatabase(
        this PaGettoApplication app,
        Action<DatabaseOptions> configure)
    {
        app.AddSqlServerDatabase();
        app.Services.Configure(configure);
        return app;
    }
}
