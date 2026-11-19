using System;
using System.Linq;
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
/// The quarantine policies of an npm feed, and the release and deletion of quarantined versions.
/// </summary>
public class QuarantineIntegrationTests : IDisposable
{
    private const string Feed = "npm";
    private const string Package = "held-pkg";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public QuarantineIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, inMemoryConfiguration: dict =>
        {
            // The policy needs the scan turned on; the database is never reached in these tests.
            dict["VulnerabilityScanning:Enabled"] = "true";
            dict["VulnerabilityScanning:ApiUrl"] = "http://localhost:1/";
        });
        _client = _app.CreateClient();
    }

    [Fact]
    public async Task QuarantinesAPublishedVersionListedAsMalicious()
    {
        await _app.CreateNpmFeedAsync(Feed, configure: f => f.QuarantineMaliciousPackages = true);
        await ListAsMaliciousAsync();

        await PublishAsync("1.0.0");

        Assert.Equal(HttpStatusCode.NotFound, await GetTarballStatusAsync("1.0.0"));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync($"feeds/{Feed}/npm/{Package}"));

        var quarantined = Assert.Single(await ListQuarantinedAsync());
        Assert.Equal(Package, quarantined.Name);
        Assert.Equal("1.0.0", quarantined.Version);
        Assert.Contains("MAL-2099-0002", quarantined.Reason, StringComparison.Ordinal);

        var audit = Assert.Single(await GetAuditEventsAsync(QuarantinePolicy.QuarantinedEvent));
        Assert.Equal(Feed, audit.Feed);
        Assert.Equal(QuarantinePolicy.Actor, audit.Actor);
    }

    [Fact]
    public async Task ServesAVersionTheFeedHasNoPolicyFor()
    {
        await _app.CreateNpmFeedAsync(Feed);
        await ListAsMaliciousAsync();

        await PublishAsync("1.0.0");

        Assert.Equal(HttpStatusCode.OK, await GetTarballStatusAsync("1.0.0"));
        Assert.Empty(await ListQuarantinedAsync());
    }

    [Fact]
    public async Task ServesAReleasedVersion()
    {
        await _app.CreateNpmFeedAsync(Feed, configure: f => f.QuarantineMaliciousPackages = true);
        await ListAsMaliciousAsync();
        await PublishAsync("1.0.0");
        var quarantined = Assert.Single(await ListQuarantinedAsync());

        using (var scope = _app.Services.CreateScope())
        {
            var released = await scope.ServiceProvider.GetRequiredService<QuarantineService>()
                .ReleaseAsync(quarantined.IsNuGet, quarantined.Key, "admin", CancellationToken.None);
            Assert.NotNull(released);
        }

        Assert.Equal(HttpStatusCode.OK, await GetTarballStatusAsync("1.0.0"));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync($"feeds/{Feed}/npm/{Package}"));
        Assert.Empty(await ListQuarantinedAsync());
        var audit = Assert.Single(await GetAuditEventsAsync(QuarantinePolicy.ReleasedEvent));
        Assert.Equal("admin", audit.Actor);
    }

    [Fact]
    public async Task DeletesAQuarantinedVersion()
    {
        await _app.CreateNpmFeedAsync(Feed, configure: f => f.QuarantineMaliciousPackages = true);
        await ListAsMaliciousAsync();
        await PublishAsync("1.0.0");
        var quarantined = Assert.Single(await ListQuarantinedAsync());

        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<QuarantineService>()
                .DeleteAsync(quarantined.IsNuGet, quarantined.Key, "admin", CancellationToken.None);
        }

        Assert.Empty(await ListQuarantinedAsync());
        using (var scope = _app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            Assert.False(await context.ArtifactVersions.IgnoreQueryFilters().AnyAsync(v => v.Artifact.Name == Package));
        }

        // The version can be published again.
        await PublishAsync("1.0.0");
    }

    [Fact]
    public async Task ANewVersionWaitsForItsScan()
    {
        await _app.CreateNpmFeedAsync(Feed, configure: f => f.QuarantineSeverity = VulnerabilitySeverity.High);
        await PublishAsync("1.0.0");
        await PublishAsync("2.0.0");

        Assert.Equal(HttpStatusCode.NotFound, await GetTarballStatusAsync("1.0.0"));
        Assert.All(await ListQuarantinedAsync(), q => Assert.Equal(QuarantinePolicy.ScanPendingReason, q.Reason));

        // The scan finds a critical vulnerability in 1.0.0 and nothing in 2.0.0.
        using (var scope = _app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            var versions = await context.ArtifactVersions.IgnoreQueryFilters().Where(v => v.Artifact.Name == Package).ToListAsync();
            var vulnerable = versions.Single(v => v.Version == "1.0.0");
            context.PackageVulnerabilities.Add(new PackageVulnerability
            {
                FeedId = (await context.Feeds.SingleAsync(f => f.Slug == Feed)).Id,
                ArtifactVersionKey = vulnerable.Key,
                PackageName = Package,
                PackageVersion = "1.0.0",
                AdvisoryId = "GHSA-test-0001",
                Severity = VulnerabilitySeverity.Critical,
                FoundAtUtc = DateTime.UtcNow,
            });
            await context.SaveChangesAsync(CancellationToken.None);

            await scope.ServiceProvider.GetRequiredService<QuarantinePolicy>()
                .ApplyScanResultsAsync([], versions.Select(v => v.Key).ToList(), CancellationToken.None);
        }

        Assert.Equal(HttpStatusCode.NotFound, await GetTarballStatusAsync("1.0.0"));
        Assert.Equal(HttpStatusCode.OK, await GetTarballStatusAsync("2.0.0"));

        var quarantined = Assert.Single(await ListQuarantinedAsync());
        Assert.Equal("1.0.0", quarantined.Version);
        Assert.Contains("GHSA-test-0001", quarantined.Reason, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private async Task PublishAsync(string version)
    {
        using var publish = await _client.PublishAsync(Feed, Package, version);
        publish.EnsureSuccessStatusCode();
    }

    private Task<HttpStatusCode> GetTarballStatusAsync(string version)
    {
        return GetStatusAsync($"feeds/{Feed}/npm/{Package}/-/{Package}-{version}.tgz");
    }

    private async Task<HttpStatusCode> GetStatusAsync(string url)
    {
        using var response = await _client.GetAsync(url);
        return response.StatusCode;
    }

    private async Task ListAsMaliciousAsync()
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        context.MaliciousPackages.Add(new MaliciousPackage
        {
            Ecosystem = "npm",
            Name = Package,
            AdvisoryId = "MAL-2099-0002",
            Summary = "Malicious code in held-pkg (npm)",
            ModifiedUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<QuarantinedVersion[]> ListQuarantinedAsync()
    {
        using var scope = _app.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<QuarantineService>().ListAsync(CancellationToken.None)).ToArray();
    }

    private async Task<AuditEvent[]> GetAuditEventsAsync(string eventName)
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        return await context.AuditEvents.Where(e => e.Event == eventName).ToArrayAsync();
    }
}
