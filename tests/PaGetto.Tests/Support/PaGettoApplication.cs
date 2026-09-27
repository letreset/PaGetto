using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Indexing;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Abstractions;

namespace PaGetto.Tests.Support;

public class PaGettoApplication : WebApplicationFactory<Startup>
{
    private readonly ITestOutputHelper _output;
    private readonly HttpMessageHandler _upstreamHandler;
    private readonly Action<Dictionary<string, string>> _inMemoryConfiguration;
    private readonly IReadOnlyList<string> _upstreamSources;

    public PaGettoApplication(
        ITestOutputHelper output,
        HttpMessageHandler upstreamHandler = null,
        Action<Dictionary<string, string>> inMemoryConfiguration = null,
        IReadOnlyList<string> upstreamSources = null)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _upstreamHandler = upstreamHandler;
        this._inMemoryConfiguration = inMemoryConfiguration;
        _upstreamSources = upstreamSources ?? ["http://localhost/v3/index.json"];
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Create temporary storage paths.
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            "PaGettoTests",
            Guid.NewGuid().ToString("N"));
        var sqlitePath = Path.Combine(tempPath, "PaGetto.db");
        var storagePath = Path.Combine(tempPath, "Packages");

        Directory.CreateDirectory(tempPath);

        builder
            .UseStartup<Startup>()
            .UseStaticWebAssets()
            .UseEnvironment("Production")
            .ConfigureLogging(logging =>
            {
                // PaGetto uses console logging by default. This logger throws operation
                // cancelled exceptions when the host shuts down, causing the the debugger
                // to pause repeatedly if CLR exceptions are enabled.
                logging.ClearProviders();

                // Pipe logs to the xunit output.
                logging.AddProvider(new XunitLoggerProvider(_output));
            })
            .ConfigureAppConfiguration(config =>
            {
                // Setup the integration test configuration.
                var dict = new Dictionary<string, string>
                {
                    { "Database:Type", "Sqlite" },
                    { "Database:ConnectionString", $"Data Source={sqlitePath}" },
                    { "Storage:Type", "FileSystem" },
                    { "Storage:Path", storagePath },
                    { "Search:Type", "Database" },
                    { "Authentication:Mode", "Legacy" },
                };
                _inMemoryConfiguration?.Invoke(dict);

                config.AddInMemoryCollection(dict);

            })
            .ConfigureServices((context, services) =>
            {
                // Make time deterministic for testing purposes.
                var time = new Mock<SystemTime>();
                time
                    .Setup(t => t.UtcNow)
                    .Returns(DateTime.Parse("2020-01-01T00:00:00.000Z"));

                services.AddSingleton(time.Object);
                if (_upstreamHandler != null)
                {
                    services.AddSingleton(_upstreamHandler);
                }

                services.Configure<HealthCheckServiceOptions>(opts => opts.Registrations.Clear());

                // The test server uses HTTP, so cookies must not require HTTPS.
                services.PostConfigure<CookieAuthenticationOptions>(
                    AuthenticationConstants.CookieScheme,
                    options => options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest);

                // Setup the integration test database.
                var provider = services.BuildServiceProvider();
                var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

                // Ensure the database is created before we run migrations. The migrations
                // can create the database too, however, migrations check whether the database exists
                // first. The SQLite provider implements this by attempting to open a connection,
                // and if that fails, creating the database. This throws several exceptions that
                // pauses the debugger repeatedly if CLR exceptions are enabled.
                using var scope = scopeFactory.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<IContext>();
                var dbCreator = ctx.Database.GetService<IRelationalDatabaseCreator>();

                dbCreator.Create();
                ctx.Database.Migrate();

                var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();
                feedService.EnsureDefaultFeedExistsAsync(CancellationToken.None).GetAwaiter().GetResult();

                // When an upstream handler is provided, configure the default feed to mirror each
                // upstream source, in order, so that the per-feed UpstreamClientFactory returns a live client.
                if (_upstreamHandler != null)
                {
                    var defaultFeed = feedService.GetDefaultFeedAsync(CancellationToken.None).GetAwaiter().GetResult();
                    for (var i = 0; i < _upstreamSources.Count; i++)
                    {
                        defaultFeed.Mirrors.Add(new FeedMirror
                        {
                            SortOrder = i,
                            Enabled = true,
                            PackageSource = _upstreamSources[i],
                        });
                    }
                    feedService.UpdateFeedAsync(defaultFeed, CancellationToken.None).GetAwaiter().GetResult();
                }
            });
    }
}

internal static class PaGettoWebApplicationFactoryExtensions
{
    public static async Task<Feed> CreateFeedAsync(
        this WebApplicationFactory<Startup> factory,
        string slug,
        string name = null,
        CancellationToken cancellationToken = default)
    {
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();

        using var scope = scopeFactory.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        return await feedService.CreateFeedAsync(new Feed
        {
            Slug = slug,
            Name = name ?? slug,
        }, cancellationToken);
    }

    public static async Task AddPackageToFeedAsync(
        this WebApplicationFactory<Startup> factory,
        Stream package,
        string feedSlug,
        CancellationToken cancellationToken = default)
    {
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();

        using var scope = scopeFactory.CreateScope();
        var indexer = scope.ServiceProvider.GetRequiredService<IPackageIndexingService>();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();
        var feed = await feedService.GetFeedBySlugAsync(feedSlug, cancellationToken)
            ?? throw new InvalidOperationException($"Feed '{feedSlug}' not found.");

        var result = await indexer.IndexAsync(feed.Id, feed.Slug, package, cacheFeedUrl: null, published: null, cancellationToken);
        if (result != PackageIndexingResult.Success)
        {
            throw new InvalidOperationException($"Unexpected indexing result {result}");
        }
    }

    public static async Task AddPackageAsync(
        this WebApplicationFactory<Startup> factory,
        Stream package,
        CancellationToken cancellationToken = default)
    {
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();

        using var scope = scopeFactory.CreateScope();
        var indexer = scope.ServiceProvider.GetRequiredService<IPackageIndexingService>();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();
        var feed = await feedService.GetDefaultFeedAsync(cancellationToken);

        var result = await indexer.IndexAsync(feed.Id, feed.Slug, package, cacheFeedUrl: null, published: null, cancellationToken);
        if (result != PackageIndexingResult.Success)
        {
            throw new InvalidOperationException($"Unexpected indexing result {result}");
        }
    }

    public static async Task AddSymbolPackageAsync(
        this WebApplicationFactory<Startup> factory,
        Stream symbolPackage,
        CancellationToken cancellationToken = default)
    {
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();

        using var scope = scopeFactory.CreateScope();
        var indexer = scope.ServiceProvider.GetRequiredService<ISymbolIndexingService>();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();
        var feed = await feedService.GetDefaultFeedAsync(cancellationToken);

        var result = await indexer.IndexAsync(feed.Id, feed.Slug, symbolPackage, cancellationToken);
        if (result != SymbolIndexingResult.Success)
        {
            throw new InvalidOperationException($"Unexpected indexing result {result}");
        }
    }
}
