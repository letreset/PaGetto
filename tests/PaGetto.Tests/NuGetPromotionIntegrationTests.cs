using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Quarantine;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Quarantine and promotion of NuGet packages, which have their own tables.
/// </summary>
public class NuGetPromotionIntegrationTests : IDisposable
{
    private const string PackageUrl = "v3/package/testdata/1.2.3/testdata.1.2.3.nupkg";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public NuGetPromotionIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient();
    }

    [Fact]
    public async Task PromotesAPackageWithItsContent()
    {
        await _app.CreateFeedAsync("release");
        await _app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));

        using var response = await _client.PostAsJsonAsync(
            "api/v1/promote",
            new { packageId = "TestData", version = "1.2.3", targetFeed = "release", move = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = await _client.GetByteArrayAsync(PackageUrl);
        var copy = await _client.GetByteArrayAsync("feeds/release/" + PackageUrl);
        Assert.Equal(source, copy);
    }

    [Fact]
    public async Task QuarantinesAPushedPackageListedAsMalicious()
    {
        using (var scope = _app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            var feed = await context.Feeds.SingleAsync(f => f.Slug == Feed.DefaultSlug, CancellationToken.None);
            feed.QuarantineMaliciousPackages = true;
            context.MaliciousPackages.Add(new MaliciousPackage
            {
                Ecosystem = "NuGet",
                Name = "testdata",
                AdvisoryId = "MAL-2099-0004",
                ModifiedUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await _app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));

        using (var response = await _client.GetAsync(PackageUrl))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (var scope = _app.Services.CreateScope())
        {
            var quarantine = scope.ServiceProvider.GetRequiredService<QuarantineService>();
            var quarantined = Assert.Single(await quarantine.ListAsync(CancellationToken.None));
            Assert.True(quarantined.IsNuGet);
            Assert.Equal("1.2.3", quarantined.Version);

            await quarantine.ReleaseAsync(true, quarantined.Key, "admin", CancellationToken.None);
        }

        using var released = await _client.GetAsync(PackageUrl);
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
