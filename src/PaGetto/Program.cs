using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;
using PaGetto.Web.Extensions;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaGetto;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        if (!host.ValidateStartupOptions())
        {
            return;
        }

        var app = new CommandLineApplication
        {
            Name = "pagetto",
            Description = "A light-weight NuGet service",
        };

        app.HelpOption(inherited: true);

        app.Command("import", import =>
        {
            import.Command("downloads", downloads =>
            {
                downloads.OnExecuteAsync(async cancellationToken =>
                {
                    using var scope = host.Services.CreateScope();
                    var importer = scope.ServiceProvider.GetRequiredService<DownloadsImporter>();

                    await importer.ImportAsync(cancellationToken);
                });
            });
        });

        app.Option("--urls", "The URLs that PaGetto should bind to.", CommandOptionType.SingleValue);

        app.OnExecuteAsync(async cancellationToken =>
        {
            WarnAboutObsoleteSettings(host.Services);

            await host.RunMigrationsAsync(cancellationToken);

            using (var scope = host.Services.CreateScope())
            {
                var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();
                await feedService.EnsureDefaultFeedExistsAsync(cancellationToken);
            }

            using (var scope = host.Services.CreateScope())
            {
                var seeder = scope.ServiceProvider.GetRequiredService<InitialAdminSeeder>();
                await seeder.SeedAsync(cancellationToken);
            }

            await host.RunAsync(cancellationToken);
        });

        await app.ExecuteAsync(args);
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host
            .CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((ctx, config) =>
            {
                var root = Environment.GetEnvironmentVariable("PAGETTO_CONFIG_ROOT");

                if (!string.IsNullOrEmpty(root))
                {
                    config.SetBasePath(root);
                }

                AddPlatformConfigFile(config, GetPlatformConfigFilePath());

                // Optionally load secrets from files in the conventional path
                config.AddKeyPerFile("/run/secrets", optional: true);
            })
            .ConfigureWebHostDefaults(web =>
            {
                web.ConfigureKestrel(options =>
                {
                    // Remove the upload limit from Kestrel. If needed, an upload limit can
                    // be enforced by a reverse proxy server, like IIS.
                    options.Limits.MaxRequestBodySize = null;
                });

                web.UseStartup<Startup>();
            });
    }

    /// <summary>
    /// The machine-wide config file that lets IIS, Windows service and systemd installs keep
    /// their settings outside the app folder.
    /// </summary>
    public static string GetPlatformConfigFilePath()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "PaGetto",
                "appsettings.json");
        }

        return "/etc/pagetto/appsettings.json";
    }

    /// <summary>
    /// Adds the optional JSON file at <paramref name="path"/> right before the environment
    /// variables source, so it overrides the app folder's <c>appsettings*.json</c> and user
    /// secrets, while environment variables, the command line and key-per-file secrets still
    /// override it. The file is only watched for changes when its folder exists at startup,
    /// otherwise the watcher would fall back to a parent such as <c>/etc</c> and watch it recursively.
    /// </summary>
    public static void AddPlatformConfigFile(IConfigurationBuilder config, string path)
    {
        var source = new JsonConfigurationSource
        {
            Path = path,
            Optional = true,
            ReloadOnChange = Directory.Exists(Path.GetDirectoryName(path)),
        };
        source.ResolveFileProvider();

        var index = 0;
        while (index < config.Sources.Count && config.Sources[index] is not EnvironmentVariablesConfigurationSource)
        {
            index++;
        }

        config.Sources.Insert(index, source);
    }

    private static void WarnAboutObsoleteSettings(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<PaGettoOptions>>().Value;

#pragma warning disable CS0618 // Reads the legacy setting only to warn about it.
        if (options.MaxPackageSizeGiB.HasValue && !options.MaxPackageSizeMiB.HasValue)
        {
            LogObsoleteMaxPackageSizeGiB(provider.GetRequiredService<ILogger<Program>>(), options.EffectiveMaxPackageSizeMiB);
        }
#pragma warning restore CS0618

        if (provider.GetRequiredService<IConfiguration>().GetSection("Mirror").Exists())
        {
            LogGlobalMirrorIgnored(provider.GetRequiredService<ILogger<Program>>());
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The global Mirror section is no longer read. Configure mirrors per feed on Admin > Feeds and remove the section.")]
    private static partial void LogGlobalMirrorIgnored(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MaxPackageSizeGiB is obsolete; use MaxPackageSizeMiB instead (currently {MaxPackageSizeMiB} MiB).")]
    private static partial void LogObsoleteMaxPackageSizeGiB(ILogger logger, uint maxPackageSizeMiB);
}
