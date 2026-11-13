using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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
/// A Go feed that mirrors another module proxy. The upstream is a Go feed of a second PaGetto
/// instance, so the mirror client is tested against a real GOPROXY server.
/// </summary>
public class GoMirrorIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "golang";
    private const string MirrorFeed = "proxy";
    private const string Module = "example.com/Team/tool";
    private const string Pseudo = "v0.0.0-20191109021931-daa7c04131f5";

    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public GoMirrorIntegrationTests(ITestOutputHelper output)
    {
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler());

        _upstreamClient = _upstream.CreateClient();
        _downstreamClient = _downstream.CreateClient();
    }

    [Fact]
    public async Task ServesUpstreamVersionsWithoutStoringThem()
    {
        await SetUpAsync();
        await UploadUpstreamAsync("v1.0.0");

        Assert.Equal("v1.0.0\n", await _downstreamClient.GetStringAsync(GoTestClient.ProxyUrl(MirrorFeed, Module, "@v/list")));
        Assert.Equal("v1.0.0", await GetVersionOfAsync(GoTestClient.ProxyUrl(MirrorFeed, Module, "@latest")));
        Assert.Equal("v1.0.0", await GetVersionOfAsync(GoTestClient.VersionUrl(MirrorFeed, Module, "v1.0.0", ".info")));
        Assert.Equal(GoModuleZipBuilder.GoMod(Module), await _downstreamClient.GetStringAsync(GoTestClient.VersionUrl(MirrorFeed, Module, "v1.0.0", ".mod")));

        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task StoresAVersionOnTheFirstDownloadOfItsZip()
    {
        await SetUpAsync();
        var zip = GoModuleZipBuilder.Create(Module, "v1.0.0", readme: "# From upstream");
        await UploadUpstreamAsync("v1.0.0", zip);

        using var download = await _downstreamClient.GetAsync(GoTestClient.VersionUrl(MirrorFeed, Module, "v1.0.0", ".zip"));

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(zip, await download.Content.ReadAsByteArrayAsync());

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("v1.0.0", stored.Version);
        Assert.Equal("example.com/!team/tool", stored.Artifact.Name);
        Assert.Equal($"http://localhost/feeds/{UpstreamFeed}/go", stored.CachedFrom);
        Assert.True(stored.HasReadme);
        Assert.Equal(1, stored.Downloads);

        // The stored version keeps the upstream's go.mod and time.
        Assert.Equal(GoModuleZipBuilder.GoMod(Module), await _downstreamClient.GetStringAsync(GoTestClient.VersionUrl(MirrorFeed, Module, "v1.0.0", ".mod")));
        using var info = JsonDocument.Parse(await _downstreamClient.GetStringAsync(GoTestClient.VersionUrl(MirrorFeed, Module, "v1.0.0", ".info")));
        Assert.Equal("2020-01-01T00:00:00Z", info.RootElement.GetProperty("Time").GetString());

        Assert.Contains("From upstream", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/artifacts/{Uri.EscapeDataString(Module)}"));
    }

    [Fact]
    public async Task VersionsUploadedToTheMirrorAreListedNextToUpstreamOnes()
    {
        await SetUpAsync();
        await UploadUpstreamAsync("v1.0.0");

        using (await _downstreamClient.UploadModuleAsync(MirrorFeed, Module, "v1.1.0"))
        {
        }

        Assert.Equal("v1.0.0\nv1.1.0\n", await _downstreamClient.GetStringAsync(GoTestClient.ProxyUrl(MirrorFeed, Module, "@v/list")));
        Assert.Equal("v1.1.0", await GetVersionOfAsync(GoTestClient.ProxyUrl(MirrorFeed, Module, "@latest")));
    }

    [Fact]
    public async Task PseudoVersionsOfTheUpstreamComeFromItsLatest()
    {
        await SetUpAsync();
        await UploadUpstreamAsync(Pseudo);

        using var list = await _downstreamClient.GetAsync(GoTestClient.ProxyUrl(MirrorFeed, Module, "@v/list"));

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal("", await list.Content.ReadAsStringAsync());
        Assert.Equal(Pseudo, await GetVersionOfAsync(GoTestClient.ProxyUrl(MirrorFeed, Module, "@latest")));

        using var zip = await _downstreamClient.GetAsync(GoTestClient.VersionUrl(MirrorFeed, Module, Pseudo, ".zip"));
        Assert.Equal(HttpStatusCode.OK, zip.StatusCode);
    }

    [Theory]
    [InlineData("@v/list")]
    [InlineData("@latest")]
    [InlineData("@v/v1.0.0.info")]
    [InlineData("@v/v1.0.0.mod")]
    [InlineData("@v/v1.0.0.zip")]
    public async Task UnknownModuleIsNotFound(string file)
    {
        await SetUpAsync();

        using var response = await _downstreamClient.GetAsync(GoTestClient.ProxyUrl(MirrorFeed, "example.com/unknown", file));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task SetUpAsync()
    {
        await _upstream.CreateGoFeedAsync(UpstreamFeed);
        await _downstream.CreateGoFeedAsync(MirrorFeed, mirrorSource: $"http://localhost/feeds/{UpstreamFeed}/go");
    }

    private async Task UploadUpstreamAsync(string version, byte[] zip = null)
    {
        using var response = await _upstreamClient.UploadModuleAsync(UpstreamFeed, Module, version, zip);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<string> GetVersionOfAsync(string url)
    {
        using var info = JsonDocument.Parse(await _downstreamClient.GetStringAsync(url));
        return info.RootElement.GetProperty("Version").GetString();
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
