using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
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
            await host.RunMigrationsAsync(cancellationToken);

            using (var scope = host.Services.CreateScope())
            {
                var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();
                await feedService.EnsureDefaultFeedExistsAsync(cancellationToken);
                await MigrateGlobalMirrorConfigToDefaultFeedAsync(scope.ServiceProvider, cancellationToken);
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

    /// <summary>
    /// One-time upgrade helper: if the global Mirror config has Enabled=true and the default
    /// feed has no mirrors yet, copy it as the default feed's first mirror. The guard checks for
    /// any mirror row, enabled or not, so an admin who later disables the mirror on the default
    /// feed won't have it silently re-enabled on the next startup.
    /// </summary>
    private static async Task MigrateGlobalMirrorConfigToDefaultFeedAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        var mirrorOptions = provider.GetRequiredService<IOptions<MirrorOptions>>().Value;

        if (!mirrorOptions.Enabled || mirrorOptions.PackageSource == null)
            return;

        var feedService = provider.GetRequiredService<IFeedService>();
        var logger = provider.GetRequiredService<ILogger<Program>>();

        var defaultFeed = await feedService.GetDefaultFeedAsync(cancellationToken);
        if (defaultFeed == null)
        {
            LogDefaultFeedNotFound(logger);
            return;
        }

        // Guard: already migrated. Any mirror row counts, including a disabled one; otherwise an
        // admin who intentionally disabled mirroring would see it re-enabled on every startup
        // as long as the obsolete global Mirror config still exists in appsettings.
        if (defaultFeed.Mirrors.Count > 0)
        {
            LogMirrorAlreadyMigrated(logger);
            return;
        }

        LogMirrorMigrationStarting(logger);

        var mirror = new FeedMirror
        {
            SortOrder = 0,
            Enabled = true,
            PackageSource = mirrorOptions.PackageSource.ToString(),
            Legacy = mirrorOptions.Legacy,
            DownloadTimeoutSeconds = mirrorOptions.PackageDownloadTimeoutSeconds,
        };

        if (mirrorOptions.Authentication is { Type: not MirrorAuthenticationType.None } auth)
        {
            mirror.AuthType = auth.Type;
            mirror.AuthUsername = auth.Username;
            mirror.AuthPassword = auth.Password;
            mirror.AuthToken = auth.Token;

            if (auth.CustomHeaders is { Count: > 0 })
            {
                mirror.AuthCustomHeaders =
                    JsonSerializer.Serialize(auth.CustomHeaders);
            }
        }

        defaultFeed.Mirrors.Add(mirror);
        await feedService.UpdateFeedAsync(defaultFeed, cancellationToken);

        LogMirrorMigrated(logger, mirror.PackageSource);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Default feed not found during mirror config migration; skipping.")]
    private static partial void LogDefaultFeedNotFound(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Default feed already has mirror settings; skipping migration.")]
    private static partial void LogMirrorAlreadyMigrated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Copying global Mirror configuration to default feed (one-time upgrade).")]
    private static partial void LogMirrorMigrationStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Global Mirror configuration copied to default feed (source: {PackageSource}).")]
    private static partial void LogMirrorMigrated(ILogger logger, string packageSource);
}
