using System;
using PaGetto.Core;
using PaGetto.Core.Configuration;
using PaGetto.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace PaGetto.Database.MySql;

public static class MySqlApplicationExtensions
{
    public static PaGettoApplication AddMySqlDatabase(this PaGettoApplication app)
    {
        app.Services.AddPaGettoDbContextProvider<MySqlContext>("MySql");

        return app;
    }

    public static PaGettoApplication AddMySqlDatabase(
        this PaGettoApplication app,
        Action<DatabaseOptions> configure)
    {
        app.AddMySqlDatabase();
        app.Services.Configure(configure);
        return app;
    }
}
