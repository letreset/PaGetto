using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Retention;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

public class RetentionCleanerTests : IDisposable
{
    private const string Feed = "dart";

    private readonly PaGettoApplication _app;

    public RetentionCleanerTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output);
    }

    [Fact]
    public async Task RemovesMirroredVersionsNotDownloadedForTheFeedsDays()
    {
        var feed = await _app.CreatePubFeedAsync(Feed, configure: f => f.MirrorCacheRetentionDays = 30);
        var now = _app.Services.GetRequiredService<SystemTime>().UtcNow;

        await AddVersionAsync(feed, "1.0.0", cachedFrom: "https://pub.dev/", lastDownloaded: now.AddDays(-31));
        await AddVersionAsync(feed, "1.1.0", cachedFrom: "https://pub.dev/", lastDownloaded: now.AddDays(-29));
        await AddVersionAsync(feed, "1.2.0", cachedFrom: "https://pub.dev/", lastDownloaded: null);
        await AddVersionAsync(feed, "2.0.0", cachedFrom: null, lastDownloaded: now.AddDays(-365));

        var removed = await RunAsync();

        // A published version stays, and one without a download time counts from now.
        Assert.Equal(1, removed);
        Assert.Equal(new[] { "1.1.0", "1.2.0", "2.0.0" }, await VersionsAsync(feed));
        Assert.Null(await ArchiveOrNullAsync(feed, "1.0.0"));
        Assert.NotNull(await ArchiveOrNullAsync(feed, "1.1.0"));
    }

    [Fact]
    public async Task KeepsMirroredVersionsWhenTheFeedKeepsThem()
    {
        var feed = await _app.CreatePubFeedAsync(Feed);
        var now = _app.Services.GetRequiredService<SystemTime>().UtcNow;
        await AddVersionAsync(feed, "1.0.0", cachedFrom: "https://pub.dev/", lastDownloaded: now.AddDays(-3650));

        Assert.Equal(0, await RunAsync());
        Assert.Equal(new[] { "1.0.0" }, await VersionsAsync(feed));
    }

    [Fact]
    public async Task RemovesVersionsBeyondTheFeedsLimits()
    {
        var feed = await _app.CreatePubFeedAsync(Feed, configure: f => f.RetentionMaxMajorVersions = 1);
        foreach (var version in new[] { "1.0.0", "2.0.0", "2.1.0" })
        {
            await AddVersionAsync(feed, version, cachedFrom: null, lastDownloaded: null);
        }

        Assert.Equal(1, await RunAsync());
        Assert.Equal(new[] { "2.0.0", "2.1.0" }, await VersionsAsync(feed));
    }

    [Fact]
    public async Task RemovesMirroredNuGetPackagesNotDownloadedForTheFeedsDays()
    {
        await _app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));
        var now = _app.Services.GetRequiredService<SystemTime>().UtcNow;

        using (var scope = _app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            await context.Feeds
                .Where(f => f.Slug == PaGetto.Core.Entities.Feed.DefaultSlug)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.MirrorCacheRetentionDays, 7));
            await context.Packages.ExecuteUpdateAsync(s => s
                .SetProperty(p => p.CachedFrom, "https://api.nuget.org/v3/index.json")
                .SetProperty(p => p.LastDownloadedAtUtc, now.AddDays(-8)));
        }

        Assert.Equal(1, await RunAsync());

        using var check = _app.Services.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<IContext>().Packages.AnyAsync());
    }

    [Fact]
    public async Task DownloadsRecordTheirTime()
    {
        var feed = await _app.CreatePubFeedAsync(Feed);
        await AddVersionAsync(feed, "1.0.0", cachedFrom: "https://pub.dev/", lastDownloaded: null);

        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IArtifactDatabase>().AddDownloadAsync(feed.Id, "my_package", "1.0.0", CancellationToken.None);
        }

        using var check = _app.Services.CreateScope();
        var version = await check.ServiceProvider.GetRequiredService<IArtifactDatabase>().FindVersionOrNullAsync(feed.Id, "my_package", "1.0.0", CancellationToken.None);
        Assert.Equal(_app.Services.GetRequiredService<SystemTime>().UtcNow, version.LastDownloadedAtUtc);
    }

    private async Task AddVersionAsync(Feed feed, string version, string cachedFrom, DateTime? lastDownloaded)
    {
        using var scope = _app.Services.CreateScope();
        var artifacts = scope.ServiceProvider.GetRequiredService<IArtifactDatabase>();
        var storage = scope.ServiceProvider.GetRequiredService<IArtifactStorageService>();

        using var archive = new MemoryStream([1, 2, 3, (byte)version[0]]);
        await ArtifactPublisher.AddAsync(
            artifacts,
            storage,
            feed,
            "my_package",
            new ArtifactVersion { Version = version, Listed = true, CachedFrom = cachedFrom, Published = DateTime.UtcNow },
            archive,
            readme: null,
            CancellationToken.None);

        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        await context.ArtifactVersions
            .Where(v => v.Artifact.FeedId == feed.Id && v.Version == version)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.LastDownloadedAtUtc, lastDownloaded));
    }

    private async Task<int> RunAsync()
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RetentionCleaner>().RunAsync(CancellationToken.None);
    }

    private async Task<string[]> VersionsAsync(Feed feed)
    {
        using var scope = _app.Services.CreateScope();
        var versions = await scope.ServiceProvider.GetRequiredService<IArtifactDatabase>().FindVersionsAsync(feed.Id, "my_package", CancellationToken.None);
        return versions.Select(v => v.Version).OrderBy(v => v, StringComparer.Ordinal).ToArray();
    }

    private async Task<Stream> ArchiveOrNullAsync(Feed feed, string version)
    {
        using var scope = _app.Services.CreateScope();
        try
        {
            return await scope.ServiceProvider.GetRequiredService<IArtifactStorageService>().GetArchiveStreamAsync(feed.Slug, "my_package", version, CancellationToken.None);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _app.Dispose();
        GC.SuppressFinalize(this);
    }
}
