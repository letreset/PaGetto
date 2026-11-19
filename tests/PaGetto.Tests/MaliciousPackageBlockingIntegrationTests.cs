using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.MaliciousPackages;
using PaGetto.Core.Webhooks;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// An npm feed that mirrors another npm registry and blocks the versions listed as malicious. The
/// upstream is an npm feed of a second PaGetto instance.
/// </summary>
public class MaliciousPackageBlockingIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "npm";
    private const string MirrorFeed = "proxy";
    private const string Package = "evil-pkg";
    private const string TarballUrl = $"feeds/{MirrorFeed}/npm/{Package}/-/{Package}-1.0.0.tgz";

    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public MaliciousPackageBlockingIntegrationTests(ITestOutputHelper output)
    {
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler(), dict => dict["MaliciousPackages:Enabled"] = "true");

        _upstreamClient = _upstream.CreateClient();
        _downstreamClient = _downstream.CreateClient();
    }

    [Fact]
    public async Task RefusesToMirrorAListedVersion()
    {
        await SetUpAsync();
        await ListAsync(affectedVersions: null);

        using var response = await _downstreamClient.GetAsync(TarballUrl);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.ReadNpmErrorAsync();
        Assert.Contains("evil-pkg 1.0.0 is blocked", error, StringComparison.Ordinal);
        Assert.Contains("MAL-2099-0001", error, StringComparison.Ordinal);
        Assert.Empty(await GetStoredVersionsAsync());

        var audit = Assert.Single(await GetAuditEventsAsync(WebhookEvent.MaliciousPackageBlockedEvent));
        Assert.Equal(MirrorFeed, audit.Feed);
        Assert.Equal(Package, audit.PackageId);
        Assert.Equal("1.0.0", audit.PackageVersion);
        Assert.Equal("advisory=MAL-2099-0001", audit.Detail);
    }

    [Fact]
    public async Task MirrorsVersionsTheListingDoesNotAffect()
    {
        await SetUpAsync();
        await ListAsync(affectedVersions: """{"versions":["2.0.0"]}""");

        using var response = await _downstreamClient.GetAsync(TarballUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DoesNotServeAMirroredCopyListedAfterItWasStored()
    {
        await SetUpAsync();
        using (var first = await _downstreamClient.GetAsync(TarballUrl))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        await ListAsync(affectedVersions: null);

        using var second = await _downstreamClient.GetAsync(TarballUrl);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
    }

    [Fact]
    public async Task ServesAVersionAnAdministratorAllowed()
    {
        await SetUpAsync();
        await ListAsync(affectedVersions: null);

        using (var scope = _downstream.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<MaliciousPackageService>()
                .AddAllowanceAsync("npm", "Evil-Pkg", "1.0.0", "False positive", "admin", CancellationToken.None);
        }

        using var response = await _downstreamClient.GetAsync(TarballUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NeverBlocksAVersionPublishedToTheFeed()
    {
        await SetUpAsync(publishUpstream: false);
        await ListAsync(affectedVersions: null);
        using (var publish = await _downstreamClient.PublishAsync(MirrorFeed, Package, "1.0.0"))
        {
            publish.EnsureSuccessStatusCode();
        }

        using var response = await _downstreamClient.GetAsync(TarballUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AFeedCanTurnTheCheckOff()
    {
        await SetUpAsync(configure: f => f.BlockMaliciousPackages = false);
        await ListAsync(affectedVersions: null);

        using var response = await _downstreamClient.GetAsync(TarballUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose()
    {
        _upstreamClient.Dispose();
        _downstreamClient.Dispose();
        _downstream.Dispose();
        _upstream.Dispose();
    }

    private async Task SetUpAsync(bool publishUpstream = true, Action<Feed> configure = null)
    {
        await _upstream.CreateNpmFeedAsync(UpstreamFeed);
        await _downstream.CreateNpmFeedAsync(MirrorFeed, $"http://localhost/feeds/{UpstreamFeed}/npm", configure);

        if (publishUpstream)
        {
            using var publish = await _upstreamClient.PublishAsync(UpstreamFeed, Package, "1.0.0");
            publish.EnsureSuccessStatusCode();
        }
    }

    private async Task ListAsync(string affectedVersions)
    {
        using var scope = _downstream.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        context.MaliciousPackages.Add(new MaliciousPackage
        {
            Ecosystem = "npm",
            Name = Package,
            AdvisoryId = "MAL-2099-0001",
            Summary = "Malicious code in evil-pkg (npm)",
            AffectedVersions = affectedVersions,
            ModifiedUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<ArtifactVersion[]> GetStoredVersionsAsync()
    {
        using var scope = _downstream.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        return await context.ArtifactVersions.Where(v => v.Artifact.Name == Package).ToArrayAsync();
    }

    private async Task<AuditEvent[]> GetAuditEventsAsync(string eventName)
    {
        using var scope = _downstream.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        return await context.AuditEvents.Where(e => e.Event == eventName).ToArrayAsync();
    }
}
