using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Tests.Support;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// A Cargo feed that mirrors another sparse registry. The upstream is a Cargo feed of a second
/// PaGetto instance, so the mirror client is tested against a real sparse index.
/// </summary>
public class CargoMirrorIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "rust";
    private const string MirrorFeed = "proxy";

    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public CargoMirrorIntegrationTests(ITestOutputHelper output)
    {
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler());

        _upstreamClient = _upstream.CreateClient();
        _downstreamClient = _downstream.CreateClient();
    }

    [Fact]
    public async Task ListsUpstreamVersionsWithoutStoringThem()
    {
        await SetUpAsync();
        using (await _upstreamClient.PublishCrateAsync(UpstreamFeed, "My_Crate", "1.0.0"))
        {
        }

        var line = Assert.Single(await _downstreamClient.GetIndexLinesAsync(MirrorFeed, "my_crate"));

        Assert.Equal("My_Crate", line.GetProperty("name").GetString());
        Assert.Equal("1.0.0", line.GetProperty("vers").GetString());
        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task StoresAVersionOnItsFirstDownload()
    {
        await SetUpAsync();
        var crate = CargoCrateBuilder.Create("My_Crate", "1.0.0", readme: "# From upstream");
        using (await _upstreamClient.PublishCrateAsync(UpstreamFeed, CargoCrateBuilder.Metadata("My_Crate", "1.0.0"), crate))
        {
        }

        using (await _upstreamClient.DeleteAsync($"feeds/{UpstreamFeed}/cargo/api/v1/crates/My_Crate/1.0.0/yank"))
        {
        }

        using var download = await _downstreamClient.GetAsync(CargoTestClient.DownloadUrl(MirrorFeed, "My_Crate", "1.0.0"));

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(crate, await download.Content.ReadAsByteArrayAsync());

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("1.0.0", stored.Version);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(crate)), stored.ContentHash);
        Assert.Equal($"http://localhost/feeds/{UpstreamFeed}/cargo/index", stored.CachedFrom);
        Assert.True(stored.HasReadme);
        Assert.False(stored.Listed);
        Assert.Equal(1, stored.Downloads);

        // The stored version keeps the upstream's index line.
        var line = Assert.Single(await _downstreamClient.GetIndexLinesAsync(MirrorFeed, "my_crate"));
        Assert.Equal("My_Crate", line.GetProperty("name").GetString());
        Assert.True(line.GetProperty("yanked").GetBoolean());

        Assert.Contains("From upstream", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/artifacts/my_crate"));
    }

    [Fact]
    public async Task VersionsPublishedToTheMirrorAreListedNextToUpstreamOnes()
    {
        await SetUpAsync();
        using (await _upstreamClient.PublishCrateAsync(UpstreamFeed, "my_crate", "1.0.0"))
        {
        }

        using (await _downstreamClient.PublishCrateAsync(MirrorFeed, "my_crate", "2.0.0"))
        {
        }

        var lines = await _downstreamClient.GetIndexLinesAsync(MirrorFeed, "my_crate");

        Assert.Equal(new[] { "1.0.0", "2.0.0" }, lines.Select(l => l.GetProperty("vers").GetString()));
    }

    [Fact]
    public async Task UnknownCrateIsNotFound()
    {
        await SetUpAsync();

        using var index = await _downstreamClient.GetAsync(CargoTestClient.IndexFileUrl(MirrorFeed, "unknown"));
        using var download = await _downstreamClient.GetAsync(CargoTestClient.DownloadUrl(MirrorFeed, "unknown", "1.0.0"));

        Assert.Equal(HttpStatusCode.NotFound, index.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    private async Task SetUpAsync()
    {
        await _upstream.CreateCargoFeedAsync(UpstreamFeed);
        await _downstream.CreateCargoFeedAsync(MirrorFeed, mirrorSource: $"http://localhost/feeds/{UpstreamFeed}/cargo/index");
    }

    private async Task<ArtifactVersion[]> GetStoredVersionsAsync()
    {
        using var scope = _downstream.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return await context.ArtifactVersions.AsNoTracking().ToArrayAsync();
    }

    public void Dispose()
    {
        _upstreamClient.Dispose();
        _downstreamClient.Dispose();
        _upstream.Dispose();
        _downstream.Dispose();
    }
}
