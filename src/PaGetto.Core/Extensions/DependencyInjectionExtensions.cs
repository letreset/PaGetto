using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using PaGetto.Core.Audit;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Content;
using PaGetto.Core.Email;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Indexing;
using PaGetto.Core.Metadata;
using PaGetto.Core.Notifications;
using PaGetto.Core.Search;
using PaGetto.Core.ServiceIndex;
using PaGetto.Core.Statistics;
using PaGetto.Core.Storage;
using PaGetto.Core.Upstream;
using PaGetto.Core.Upstream.Clients;
using PaGetto.Core.Validation;
using PaGetto.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace PaGetto.Core.Extensions;

public static partial class DependencyInjectionExtensions
{
    public static PaGettoApplication AddPaGettoApplication(
        this IServiceCollection services,
        Action<PaGettoApplication> configureAction)
    {
        var app = new PaGettoApplication(services);

        services.AddConfiguration();
        services.AddPaGettoServices();
        services.AddDefaultProviders();

        configureAction(app);

        services.AddFallbackServices();

        return app;
    }

    /// <summary>
    /// Configures and validates options.
    /// </summary>
    /// <typeparam name="TOptions">The options type that should be added.</typeparam>
    /// <param name="services">The dependency injection container to add options.</param>
    /// <param name="key">
    /// The configuration key that should be used when configuring the options.
    /// If null, the root configuration will be used to configure the options.
    /// </param>
    /// <returns>The dependency injection container.</returns>
    public static IServiceCollection AddPaGettoOptions<TOptions>(
        this IServiceCollection services,
        string key = null)
        where TOptions : class
    {
        services.AddSingleton<IValidateOptions<TOptions>>(new ValidatePaGettoOptions<TOptions>(key));
        services.AddSingleton<IConfigureOptions<TOptions>>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            if (key != null)
            {
                config = config.GetSection(key);
            }

            return new BindOptions<TOptions>(config);
        });

        return services;
    }

    private static void AddConfiguration(this IServiceCollection services)
    {
        services.AddPaGettoOptions<PaGettoOptions>();
        services.AddPaGettoOptions<DatabaseOptions>(nameof(PaGettoOptions.Database));
        services.AddPaGettoOptions<FileSystemStorageOptions>(nameof(PaGettoOptions.Storage));
        services.AddPaGettoOptions<RetentionOptions>(nameof(PaGettoOptions.Retention));
        services.AddPaGettoOptions<SearchOptions>(nameof(PaGettoOptions.Search));
        services.AddPaGettoOptions<StorageOptions>(nameof(PaGettoOptions.Storage));
        services.AddPaGettoOptions<StatisticsOptions>(nameof(PaGettoOptions.Statistics));
        services.AddPaGettoOptions<NugetAuthenticationOptions>(nameof(PaGettoOptions.Authentication));
        services.AddPaGettoOptions<EmailOptions>(nameof(PaGettoOptions.Email));
        services.AddPaGettoOptions<SmtpEmailOptions>(nameof(PaGettoOptions.Email));
        services.AddPaGettoOptions<PatExpiryNotificationOptions>(nameof(PaGettoOptions.PatExpiryNotification));
        services.AddPaGettoOptions<AuditOptions>(nameof(PaGettoOptions.Audit));
    }

    private static void AddPaGettoServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IFrameworkCompatibilityService, FrameworkCompatibilityService>();
        services.TryAddSingleton<IPackageDownloadsSource, PackageDownloadsJsonSource>();

        services.TryAddSingleton<ISearchResponseBuilder, SearchResponseBuilder>();
        services.TryAddSingleton<NullSearchIndexer>();
        services.TryAddSingleton<NullSearchService>();
        services.TryAddSingleton<RegistrationBuilder>();
        services.TryAddSingleton<SystemTime>();
        services.TryAddSingleton<UpstreamListingCache>();
        services.TryAddSingleton<ValidateStartupOptions>();

        services.TryAddSingleton(HttpClientFactory);

        services.TryAddScoped<DownloadsImporter>();
        services.TryAddScoped<InitialAdminSeeder>();

        services.TryAddScoped<IFeedService, FeedService>();
        services.TryAddScoped<FeedContext>();
        services.TryAddScoped<IFeedContext>(provider => provider.GetRequiredService<FeedContext>());
        services.TryAddScoped<IFeedSettingsResolver, FeedSettingsResolver>();
        services.TryAddScoped<IUpstreamClientFactory, UpstreamClientFactory>();

        services.TryAddTransient<IAuthenticationService, ApiKeyAuthenticationService>();
        services.TryAddTransient<IUserService, UserService>();
        services.TryAddTransient<IGroupService, GroupService>();
        services.TryAddTransient<IPermissionService, PermissionService>();
        services.TryAddTransient<IAuditEventService, AuditEventService>();
        services.TryAddTransient<ITokenService, TokenService>();
        services.TryAddTransient<IFeedAuthenticationService, FeedAuthenticationService>();
        services.TryAddTransient<IPackageContentService, DefaultPackageContentService>();
        services.TryAddTransient<IPackageDeletionService, PackageDeletionService>();
        services.TryAddTransient<IPackageIndexingService, PackageIndexingService>();
        services.TryAddTransient<IPackageMetadataService, DefaultPackageMetadataService>();
        services.TryAddTransient<IPackageService, PackageService>();
        services.TryAddTransient<IPackageStorageService, PackageStorageService>();
        services.TryAddTransient<IServiceIndexService, PaGettoServiceIndex>();
        services.TryAddTransient<ISymbolIndexingService, SymbolIndexingService>();
        services.TryAddTransient<ISymbolStorageService, SymbolStorageService>();
        services.TryAddTransient<IStatisticsService, StatisticsService>();

        services.TryAddTransient<DatabaseSearchService>();
        services.TryAddTransient<FileStorageService>();
        services.TryAddTransient<PackageService>();
        services.TryAddTransient<DisabledUpstreamClient>();
        services.TryAddSingleton<NullStorageService>();
        services.TryAddSingleton<PackageMirrorLock>();
        services.TryAddTransient<PackageDatabase>();

        services.TryAddSingleton<NullEmailSender>();
        services.TryAddTransient<SmtpEmailSender>();

        services.TryAddSingleton<IPatExpiryEmailBuilder, PatExpiryEmailBuilder>();
    }

    private static void AddDefaultProviders(this IServiceCollection services)
    {
        services.AddProvider((provider, configuration) =>
        {
            if (!configuration.HasSearchType("null")) return null;

            return provider.GetRequiredService<NullSearchService>();
        });

        services.AddProvider((provider, configuration) =>
        {
            if (!configuration.HasSearchType("null")) return null;

            return provider.GetRequiredService<NullSearchIndexer>();
        });

        services.AddProvider<IStorageService>((provider, configuration) =>
        {
            if (configuration.HasStorageType("filesystem"))
            {
                return provider.GetRequiredService<FileStorageService>();
            }

            if (configuration.HasStorageType("null"))
            {
                return provider.GetRequiredService<NullStorageService>();
            }

            return null;
        });

        services.AddProvider<IEmailSender>((provider, configuration) =>
        {
            if (configuration.HasEmailType("smtp"))
            {
                return provider.GetRequiredService<SmtpEmailSender>();
            }

            // Email is optional: fall back to the no-op sender when it is
            // explicitly disabled or simply not configured.
            if (configuration.HasEmailType("null") || string.IsNullOrEmpty(configuration[_emailTypeKey]))
            {
                return provider.GetRequiredService<NullEmailSender>();
            }

            return null;
        });
    }

    private static void AddFallbackServices(this IServiceCollection services)
    {
        services.TryAddScoped<IContext, NullContext>();

        // PaGetto's services have multiple implementations that live side-by-side.
        // The application will choose the implementation using one of two ways:
        //
        // 1. Using the first implementation that was registered in the dependency injection
        //    container. This is the strategy used by applications that embed PaGetto.
        // 2. Using "providers". The providers will examine the application's configuration to
        //    determine whether its service implementation is active. Thsi is the strategy used
        //    by the default PaGetto application.
        //
        // PaGetto has database and search services, but the database services are special
        // in that they may also act as search services. If an application registers the
        // database service first and the search service second, the application should
        // use the search service even though it wasn't registered first. Furthermore,
        // if an application registers a database service without a search service, the
        // database service should be used for search. This effect is achieved by deferring
        // the database search service's registration until the very end.
        services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());
        services.TryAddTransient<ISearchService>(provider => provider.GetRequiredService<DatabaseSearchService>());

        services.TryAddTransient<IEmailSender>(provider => provider.GetRequiredService<NullEmailSender>());
    }

    private static HttpClient HttpClientFactory(IServiceProvider provider)
    {
        var assembly = Assembly.GetEntryAssembly();
        var assemblyName = assembly.GetName().Name;
        var assemblyVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        });

        client.DefaultRequestHeaders.Add("User-Agent", $"{assemblyName}/{assemblyVersion}");
        // The same timeout as a feed mirror without its own download timeout.
        client.Timeout = TimeSpan.FromSeconds(new MirrorOptions().PackageDownloadTimeoutSeconds);

        return client;
    }
}
