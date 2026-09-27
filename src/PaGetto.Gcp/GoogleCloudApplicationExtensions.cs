using System;
using PaGetto.Core;
using PaGetto.Core.Configuration;
using PaGetto.Core.Extensions;
using PaGetto.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PaGetto.Gcp;

public static class GoogleCloudApplicationExtensions
{
    public static PaGettoApplication AddGoogleCloudStorage(this PaGettoApplication app)
    {
        app.Services.AddPaGettoOptions<GoogleCloudStorageOptions>(nameof(PaGettoOptions.Storage));
        app.Services.AddTransient<GoogleCloudStorageService>();

        app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<GoogleCloudStorageService>());

        app.Services.AddProvider<IStorageService>((provider, config) =>
        {
            if (!config.HasStorageType("GoogleCloud")) return null;

            return provider.GetRequiredService<GoogleCloudStorageService>();
        });

        return app;
    }

    public static PaGettoApplication AddGoogleCloudStorage(
        this PaGettoApplication app,
        Action<GoogleCloudStorageOptions> configure)
    {
        app.AddGoogleCloudStorage();
        app.Services.Configure(configure);
        return app;
    }
}
