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
/// Promoting a version between two npm feeds through <c>POST {feed}/api/v1/promote</c>.
/// </summary>
public class PromotionIntegrationTests : IDisposable
{
    private const string Staging = "staging";
    private const string Release = "release";
    private const string Package = "promoted-pkg";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public PromotionIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient();
    }

    [Fact]
    public async Task CopiesTheVersionWithItsContent()
    {
        await SetUpAsync();

        using var response = await PromoteAsync(Release, move: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = await _client.GetByteArrayAsync(TarballUrl(Staging));
        var copy = await _client.GetByteArrayAsync(TarballUrl(Release));
        Assert.Equal(source, copy);

        var versions = await GetStoredVersionsAsync();
        Assert.Equal(2, versions.Length);
        Assert.Single(versions.Select(v => v.ContentHash).Distinct());
        Assert.Single(versions.Select(v => v.MetadataJson).Distinct());

        var audit = Assert.Single(await GetAuditEventsAsync(PromotionService.PromotedEvent));
        Assert.Equal(Release, audit.Feed);
        Assert.Contains(Staging, audit.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MovesTheVersion()
    {
        await SetUpAsync();

        using var response = await PromoteAsync(Release, move: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var source = await _client.GetAsync(TarballUrl(Staging));
        Assert.Equal(HttpStatusCode.NotFound, source.StatusCode);
        using var copy = await _client.GetAsync(TarballUrl(Release));
        Assert.Equal(HttpStatusCode.OK, copy.StatusCode);
    }

    [Fact]
    public async Task RefusesAVersionTheTargetHas()
    {
        await SetUpAsync();
        using (var first = await PromoteAsync(Release, move: false))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var second = await PromoteAsync(Release, move: false);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task RefusesAFeedOfAnotherType()
    {
        await SetUpAsync();

        using var response = await PromoteAsync("default", move: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnswersMissingFieldsAsNotFound()
    {
        await SetUpAsync();

        using var noTarget = await _client.PostAsJsonAsync($"feeds/{Staging}/api/v1/promote", new { packageId = Package, version = "1.0.0" });
        using var noPackage = await _client.PostAsJsonAsync($"feeds/{Staging}/api/v1/promote", new { targetFeed = Release });

        Assert.Equal(HttpStatusCode.NotFound, noTarget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noPackage.StatusCode);
    }

    [Fact]
    public async Task AppliesTheTargetFeedsPolicies()
    {
        await SetUpAsync(configureRelease: f => f.QuarantineMaliciousPackages = true);
        using (var scope = _app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            context.MaliciousPackages.Add(new MaliciousPackage
            {
                Ecosystem = "npm",
                Name = Package,
                AdvisoryId = "MAL-2099-0003",
                ModifiedUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            await context.SaveChangesAsync(CancellationToken.None);
        }

        using var response = await PromoteAsync(Release, move: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var copy = await _client.GetAsync(TarballUrl(Release));
        Assert.Equal(HttpStatusCode.NotFound, copy.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private static string TarballUrl(string feed)
    {
        return $"feeds/{feed}/npm/{Package}/-/{Package}-1.0.0.tgz";
    }

    private async Task SetUpAsync(Action<Feed> configureRelease = null)
    {
        await _app.CreateNpmFeedAsync(Staging);
        await _app.CreateNpmFeedAsync(Release, configure: configureRelease);

        using var publish = await _client.PublishAsync(Staging, Package, "1.0.0");
        publish.EnsureSuccessStatusCode();
    }

    private Task<HttpResponseMessage> PromoteAsync(string target, bool move)
    {
        return _client.PostAsJsonAsync(
            $"feeds/{Staging}/api/v1/promote",
            new { packageId = Package, version = "1.0.0", targetFeed = target, move });
    }

    private async Task<ArtifactVersion[]> GetStoredVersionsAsync()
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        return await context.ArtifactVersions.Where(v => v.Artifact.Name == Package).ToArrayAsync();
    }

    private async Task<AuditEvent[]> GetAuditEventsAsync(string eventName)
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        return await context.AuditEvents.Where(e => e.Event == eventName).ToArrayAsync();
    }
}
