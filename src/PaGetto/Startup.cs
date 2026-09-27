using System;
using PaGetto.Aliyun;
using PaGetto.Aws;
using PaGetto.Azure;
using PaGetto.DataProtection;
using PaGetto.Core;
using PaGetto.Core.Audit;
using PaGetto.Core.Configuration;
using PaGetto.Core.Email;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Indexing;
using PaGetto.Core.Notifications;
using PaGetto.Core.Search;
using PaGetto.Core.Storage;
using PaGetto.Database.MySql;
using PaGetto.Database.PostgreSql;
using PaGetto.Database.Sqlite;
using PaGetto.Database.SqlServer;
using PaGetto.Gcp;
using PaGetto.Tencent;
using PaGetto.Web;
using PaGetto.Web.Extensions;
using PaGetto.Web.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using HealthCheckOptions = Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions;

namespace PaGetto;

public class Startup
{
    private IConfiguration Configuration { get; }
    private IWebHostEnvironment Environment { get; }

    public Startup(IConfiguration configuration, IWebHostEnvironment environment)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.ConfigureOptions<ValidatePaGettoOptions>();
        services.ConfigureOptions<ConfigurePaGettoServer>();

        services.AddPaGettoOptions<IISServerOptions>(nameof(IISServerOptions));
        services.AddPaGettoWebApplication(ConfigurePaGettoApplication);

        // You can swap between implementations of subsystems like storage and search using PaGetto's configuration.
        // Each subsystem's implementation has a provider that reads the configuration to determine if it should be
        // activated. PaGetto will run through all its providers until it finds one that is active.
        services.AddScoped(DependencyInjectionExtensions.GetServiceFromProviders<IContext>);
        services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IStorageService>);
        services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IPackageDatabase>);
        services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchService>);
        services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchIndexer>);
        services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IEmailSender>);

        // Emails token owners as their personal access tokens approach expiry.
        services.AddHostedService<PatExpiryNotificationService>();

        // Fills size, copyright and license expression of packages stored before they were recorded.
        services.AddHostedService<PackageMetadataBackfillService>();

        // Deletes audit events older than Audit:RetentionDays.
        services.AddHostedService<AuditRetentionService>();

        services.AddHealthChecks();

        services.AddCors();

        // Configured by ConfigurePaGettoServer; only used when RequestRateLimit:Enabled is true.
        services.AddRateLimiter(_ => { });

        // text/html is left out on purpose: compressing pages that carry antiforgery tokens
        // over HTTPS enables BREACH-style attacks. The NuGet API is JSON.
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes = ["application/json", "text/css", "text/javascript", "application/javascript", "image/svg+xml"];
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });

        var securityHeaders = Configuration.GetSection(nameof(PaGettoOptions.SecurityHeaders)).Get<SecurityHeadersOptions>() ?? new SecurityHeadersOptions();
        services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(securityHeaders.HstsMaxAgeDays));

        ConfigureDataProtection(services, Configuration.GetSection(nameof(PaGettoOptions.DataProtection)).Get<KeyProtectionOptions>());
    }

    private static void ConfigureDataProtection(IServiceCollection services, KeyProtectionOptions keyProtection)
    {
        // Persist the Data Protection key ring through PaGetto's storage abstraction so it survives
        // container restarts and new revisions, for every storage backend (FileSystem, Azure Blob,
        // S3, GCS, OSS, COS). Without persistence the key ring lives in the container's ephemeral
        // filesystem and is regenerated on every restart, invalidating all existing antiforgery and
        // auth cookies — form POSTs then fail with HTTP 400 until users clear their cookies.
        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName("PaGetto");

        // Encrypt the keys at rest when a certificate is configured. The same certificate decrypts
        // them, also when it was loaded from a file rather than from a certificate store.
        var certificate = DataProtectionCertificate.Load(keyProtection);
        if (certificate != null)
        {
            dataProtection
                .ProtectKeysWithCertificate(certificate)
                .UnprotectKeysWithAnyCertificate(certificate);
        }

        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(
            sp => new ConfigureStorageXmlRepository(sp));
    }

    private void ConfigurePaGettoApplication(PaGettoApplication app)
    {
        //Add base authentication and authorization
        app.AddNugetBasicHttpAuthentication();
        app.AddNugetBasicHttpAuthorization();

        // Add Entra ID (OIDC) authentication when configured
        app.AddEntraAuthentication(Configuration, Environment);

        // Add database providers.
        app.AddMySqlDatabase();
        app.AddPostgreSqlDatabase();
        app.AddSqliteDatabase();
        app.AddSqlServerDatabase();

        // Add storage providers.
        app.AddFileStorage();
        app.AddAliyunOssStorage();
        app.AddAwsS3Storage();
        app.AddAzureBlobStorage();
        app.AddGoogleCloudStorage();
        app.AddTencentOssStorage();

        // Add search providers.
        //app.AddAzureSearch();

        // Add email providers. SMTP and the no-op sender are registered by the
        // core defaults; Graph needs its Azure client wired up here.
        app.AddGraphEmail();
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        var options = Configuration.Get<PaGettoOptions>();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseStatusCodePages();
        }

        app.UseForwardedHeaders();
        app.UsePathBase(options.PathBase);

        if (!env.IsDevelopment() && options.SecurityHeaders?.EnableHsts == true)
        {
            app.UseHsts();
        }

        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseResponseCompression();

        // Liveness probe
        // registered before FeedResolutionMiddleware so it never resolves the DbContext or other services.
        app.UseHealthChecks("/livez", new HealthCheckOptions { Predicate = _ => false });

        app.UseMiddleware<FeedStaticFilePathMiddleware>();
        app.UseStaticFiles();
        app.UseAuthentication();

        // After authentication so requests can be partitioned by user name.
        if (options.RequestRateLimit?.Enabled == true)
        {
            app.UseRateLimiter();
        }

        app.UseMiddleware<FeedResolutionMiddleware>();
        app.UseMiddleware<MustChangePasswordMiddleware>();
        app.UseRouting();
        app.UseAuthorization();

        app.UseCors(ConfigurePaGettoServer.CorsPolicy);

        app.UseOperationCancelledMiddleware();

        app.UseEndpoints(endpoints =>
        {
            var pagetto = new PaGettoEndpointBuilder();

            pagetto.MapEndpoints(endpoints);
        });

        app.UseHealthChecks(options.HealthCheck.Path,
            new HealthCheckOptions
            {
                ResponseWriter = async (context, report) =>
                {
                    await report.FormatAsJson(context.Response.Body, options.Statistics.ListConfiguredServices, options.HealthCheck.StatusPropertyName,
                        context.RequestAborted);
                },
                Predicate = check => check.IsConfigured(options)
            }
        );
    }
}
