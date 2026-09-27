using System;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PaGetto.Web.Extensions;

public static class HostExtensions
{
    public static IHostBuilder UsePaGetto(this IHostBuilder host, Action<PaGettoApplication> configure)
    {
        return host.ConfigureServices(services =>
        {
            services.AddPaGettoWebApplication(configure);
        });
    }

    public static async Task RunMigrationsAsync(
        this IHost host,
        CancellationToken cancellationToken = default)
    {
        // Run migrations if necessary.
        var options = host.Services.GetRequiredService<IOptions<PaGettoOptions>>();

        if (options.Value.RunMigrationsAtStartup)
        {
            using var scope = host.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetService<IContext>();
            if (ctx != null)
            {
                await ctx.RunMigrationsAsync(cancellationToken);
            }
        }
    }

    public static bool ValidateStartupOptions(this IHost host)
    {
        return host
            .Services
            .GetRequiredService<ValidateStartupOptions>()
            .Validate();
    }
}
