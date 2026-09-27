using System;
using PaGetto.Core.Configuration;
using PaGetto.Core.Search;
using PaGetto.Core.Statistics;
using PaGetto.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PaGetto.Core.Extensions;

public static class PaGettoApplicationExtensions
{
    public static PaGettoApplication AddFileStorage(this PaGettoApplication app)
    {
        app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<FileStorageService>());

        return app;
    }

    public static PaGettoApplication AddFileStorage(
        this PaGettoApplication app,
        Action<FileSystemStorageOptions> configure)
    {
        app.AddFileStorage();
        app.Services.Configure(configure);
        return app;
    }

    public static PaGettoApplication AddNullStorage(this PaGettoApplication app)
    {
        app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<NullStorageService>());
        return app;
    }

    public static PaGettoApplication AddNullSearch(this PaGettoApplication app)
    {
        app.Services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());
        app.Services.TryAddTransient<ISearchService>(provider => provider.GetRequiredService<NullSearchService>());
        return app;
    }

    public static PaGettoApplication AddStatistics(this PaGettoApplication app)
    {
        app.Services.TryAddSingleton<IStatisticsService, StatisticsService>();
        return app;
    }
}
