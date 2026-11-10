using System;
using System.Linq;
using System.Net;
using System.Net.Http;
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
/// A Dart feed that mirrors another pub server. The upstream is a Dart feed of a second PaGetto
/// instance, so the mirror client is tested against the real pub API.
/// </summary>
public class PubMirrorIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "dart";
    private const string MirrorFeed = "proxy";

    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public PubMirrorIntegrationTests(ITestOutputHelper output)
    {
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler());

        _upstreamClient = _upstream.CreateClient();
        _downstreamClient = _downstream.CreateClient();
    }

    [Fact]
    public async Task ListsUpstreamVersionsWithArchivesServedByTheMirror()
    {
        await SetUpAsync();
        using (await _upstreamClient.PublishAsync(UpstreamFeed, PubArchiveBuilder.Create("my_package", "1.0.0")))
        {
        }

        using var response = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/api/packages/my_package");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var version = Assert.Single((await response.ReadJsonAsync()).GetProperty("versions").EnumerateArray());
        Assert.Equal("1.0.0", version.GetProperty("version").GetString());
        Assert.Equal("my_package", version.GetProperty("pubspec").GetProperty("name").GetString());
        Assert.Equal(
            $"http://localhost/feeds/{MirrorFeed}/packages/my_package/versions/1.0.0.tar.gz",
            version.GetProperty("archive_url").GetString());

        // Listing a package doesn't store it.
        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task StoresAVersionOnItsFirstDownload()
    {
        await SetUpAsync();
        var archive = PubArchiveBuilder.Create("my_package", "1.0.0", readme: "# From upstream").ToArray();
        using (await _upstreamClient.PublishAsync(UpstreamFeed, new System.IO.MemoryStream(archive)))
        {
        }

        using var download = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/packages/my_package/versions/1.0.0.tar.gz");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(archive, await download.Content.ReadAsByteArrayAsync());

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("1.0.0", stored.Version);
        Assert.Equal($"http://localhost/feeds/{UpstreamFeed}", stored.CachedFrom);
        Assert.True(stored.HasReadme);
        Assert.Equal(1, stored.Downloads);

        // The mirrored package now shows up in the web UI of the mirror.
        Assert.Contains("my_package", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/"));
        Assert.Contains("From upstream", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/artifacts/my_package"));
    }

    [Fact]
    public async Task VersionsPublishedToTheMirrorAreListedNextToUpstreamOnes()
    {
        await SetUpAsync();
        using (await _upstreamClient.PublishAsync(UpstreamFeed, PubArchiveBuilder.Create("my_package", "1.0.0")))
        {
        }

        using (await _downstreamClient.PublishAsync(MirrorFeed, PubArchiveBuilder.Create("my_package", "2.0.0")))
        {
        }

        using var response = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/api/packages/my_package");

        var package = await response.ReadJsonAsync();
        Assert.Equal(
            new[] { "1.0.0", "2.0.0" },
            package.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("version").GetString()));
        Assert.Equal("2.0.0", package.GetProperty("latest").GetProperty("version").GetString());
    }

    [Fact]
    public async Task UnknownPackageIsNotFound()
    {
        await SetUpAsync();

        using var listing = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/api/packages/unknown");
        using var download = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/packages/unknown/versions/1.0.0.tar.gz");

        Assert.Equal(HttpStatusCode.NotFound, listing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    private async Task SetUpAsync()
    {
        await _upstream.CreatePubFeedAsync(UpstreamFeed);
        await _downstream.CreatePubFeedAsync(MirrorFeed, mirrorSource: $"http://localhost/feeds/{UpstreamFeed}");
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
