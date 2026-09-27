using PaGetto.Core;
using PaGetto.Core.Configuration;
using PaGetto.Core.Extensions;
using PaGetto.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace PaGetto.Tencent;

public static class TencentApplicationExtensions
{
    public static PaGettoApplication AddTencentOssStorage(this PaGettoApplication app)
    {
        app.Services.AddPaGettoOptions<TencentStorageOptions>(nameof(PaGettoOptions.Storage));

        app.Services.AddTransient<TencentStorageService>();
        app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<TencentStorageService>());

        app.Services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TencentStorageOptions>>().Value;

            return new TencentCosClient(options);
        });

        app.Services.AddProvider<IStorageService>((provider, config) =>
        {
            // AddProvider treats null as "not this provider"; PaGetto.Core has no nullable annotations.
            if (!config.HasStorageType("TencentCos"))
                return null!;

            return provider.GetRequiredService<TencentStorageService>();
        });

        return app;
    }

    public static PaGettoApplication AddTencentOssStorage(
        this PaGettoApplication app,
        Action<TencentStorageOptions> configure)
    {
        app.AddTencentOssStorage();
        app.Services.Configure(configure);
        return app;
    }
}
