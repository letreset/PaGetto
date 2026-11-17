using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// A CocoaPods feed that mirrors another spec repository. The upstream is a CocoaPods feed of a
/// second PaGetto instance, so the mirror client is tested against a real CDN spec repository.
/// </summary>
public class CocoaPodsMirrorIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "pods";
    private const string MirrorFeed = "proxy";
    private const string Pod = "MyLib";

    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public CocoaPodsMirrorIntegrationTests(ITestOutputHelper output)
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
        await PublishUpstreamAsync("1.0.0");
        await PublishUpstreamAsync("1.1.0");

        Assert.Equal(new[] { "1.0.0", "1.1.0" }, await _downstreamClient.GetPodVersionsAsync(MirrorFeed, Pod));
        Assert.Empty(await GetStoredVersionsAsync());

        // Pods that only the mirror has are not in all_pods.txt.
        Assert.Empty(await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/cocoapods/all_pods.txt"));
    }

    [Fact]
    public async Task StoresAPodspecOnItsFirstDownload()
    {
        await SetUpAsync();
        await PublishUpstreamAsync("1.0.0", CocoaPodsTestClient.SourceZip());
        var upstream = await _upstreamClient.GetStringAsync(CocoaPodsTestClient.PodspecUrl(UpstreamFeed, Pod, "1.0.0"));

        using var response = await _downstreamClient.GetAsync(CocoaPodsTestClient.PodspecUrl(MirrorFeed, Pod, "1.0.0"));

        // The podspec is served as the mirror serves it, so the source stays where it points.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(upstream, await response.Content.ReadAsStringAsync());
        Assert.Contains($"http://localhost/feeds/{UpstreamFeed}/cocoapods/sources/MyLib/1.0.0/MyLib-1.0.0.zip", upstream);

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("1.0.0", stored.Version);
        Assert.Equal("!my!lib", stored.Artifact.Name);
        Assert.Equal($"http://localhost/feeds/{UpstreamFeed}/cocoapods/", stored.CachedFrom);
        Assert.Equal("A test pod", stored.Description);
        Assert.Null(stored.ContentHash);

        // The stored version is served from the feed from now on.
        Assert.Equal(upstream, await _downstreamClient.GetStringAsync(CocoaPodsTestClient.PodspecUrl(MirrorFeed, Pod, "1.0.0")));
        Assert.Contains("<h1>MyLib</h1>", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/artifacts/MyLib"));
    }

    [Fact]
    public async Task VersionsPublishedToTheFeedComeFirst()
    {
        await SetUpAsync();
        await PublishUpstreamAsync("1.0.0");
        await PublishUpstreamAsync("1.1.0");

        var local = CocoaPodsTestClient.Podspec(Pod, "1.1.0", p => p["summary"] = "local");
        using (var publish = await _downstreamClient.PublishAsync(MirrorFeed, local))
        {
            Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        }

        using (var publish = await _downstreamClient.PublishAsync(MirrorFeed, CocoaPodsTestClient.Podspec(Pod, "2.0.0")))
        {
            Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        }

        Assert.Equal(new[] { "1.0.0", "1.1.0", "2.0.0" }, await _downstreamClient.GetPodVersionsAsync(MirrorFeed, Pod));
        Assert.Equal(local, await _downstreamClient.GetStringAsync(CocoaPodsTestClient.PodspecUrl(MirrorFeed, Pod, "1.1.0")));
    }

    [Fact]
    public async Task UnlistedVersionOfTheFeedHidesTheMirrorsVersion()
    {
        await SetUpAsync();
        await PublishUpstreamAsync("1.0.0");
        await PublishUpstreamAsync("1.1.0");
        using (await _downstreamClient.GetAsync(CocoaPodsTestClient.PodspecUrl(MirrorFeed, Pod, "1.1.0")))
        {
        }

        using (var scope = _downstream.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            var stored = await context.ArtifactVersions.SingleAsync();
            stored.Listed = false;
            await context.SaveChangesAsync(default);
        }

        Assert.Equal(new[] { "1.0.0" }, await _downstreamClient.GetPodVersionsAsync(MirrorFeed, Pod));
    }

    [Theory]
    [InlineData("Unknown", "1.0.0")]
    [InlineData(Pod, "9.9.9")]
    public async Task UnknownPodOrVersionIsNotFound(string name, string version)
    {
        await SetUpAsync();
        await PublishUpstreamAsync("1.0.0");

        using var response = await _downstreamClient.GetAsync(CocoaPodsTestClient.PodspecUrl(MirrorFeed, name, version));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await GetStoredVersionsAsync());
    }

    private async Task SetUpAsync()
    {
        await _upstream.CreateCocoaPodsFeedAsync(UpstreamFeed);
        await _downstream.CreateCocoaPodsFeedAsync(MirrorFeed, mirrorSource: $"http://localhost/feeds/{UpstreamFeed}/cocoapods/");
    }

    private async Task PublishUpstreamAsync(string version, byte[] source = null)
    {
        using var response = await _upstreamClient.PublishAsync(UpstreamFeed, CocoaPodsTestClient.Podspec(Pod, version), source, "MyLib.zip");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<ArtifactVersion[]> GetStoredVersionsAsync()
    {
        using var scope = _downstream.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return await context.ArtifactVersions.AsNoTracking().Include(v => v.Artifact).ToArrayAsync();
    }

    public void Dispose()
    {
        _upstreamClient.Dispose();
        _downstreamClient.Dispose();
        _upstream.Dispose();
        _downstream.Dispose();
    }
}
