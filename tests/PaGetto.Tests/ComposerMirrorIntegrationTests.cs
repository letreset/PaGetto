using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
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
/// A Composer feed that mirrors another Composer repository. The upstream is a Composer feed of a
/// second PaGetto instance, so the mirror client is tested against a real Composer repository.
/// </summary>
public class ComposerMirrorIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "php";
    private const string MirrorFeed = "proxy";
    private const string Package = "acme/hello";

    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public ComposerMirrorIntegrationTests(ITestOutputHelper output)
    {
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler());

        _upstreamClient = _upstream.CreateClient();
        _downstreamClient = _downstream.CreateClient();
    }

    [Fact]
    public async Task ListsUpstreamVersionsThroughTheFeedWithoutStoringThem()
    {
        await SetUpAsync();
        var zip = ComposerZipBuilder.Create(Package);
        var sha1 = Convert.ToHexStringLower(SHA1.HashData(zip));
        await UploadUpstreamAsync("1.0.0", zip);

        var version = Assert.Single(await _downstreamClient.GetVersionsAsync(MirrorFeed, Package))!;

        Assert.Equal("1.0.0", version["version"]!.GetValue<string>());
        Assert.Equal(">=8.1", version["require"]!["php"]!.GetValue<string>());
        Assert.Equal($"http://localhost/feeds/{MirrorFeed}/composer/dists/{Package}/1.0.0/{sha1}.zip", version["dist"]!["url"]!.GetValue<string>());
        Assert.Equal(sha1, version["dist"]!["reference"]!.GetValue<string>());
        Assert.Equal(sha1, version["dist"]!["shasum"]!.GetValue<string>());

        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task StoresAVersionOnTheFirstDownloadOfItsZip()
    {
        await SetUpAsync();
        var zip = ComposerZipBuilder.Create(Package, readme: "# From upstream");
        await UploadUpstreamAsync("v1.0.0", zip);
        var listed = (await _downstreamClient.GetVersionsAsync(MirrorFeed, Package)).Single()!.ToJsonString();
        var url = JsonNode.Parse(listed)!["dist"]!["url"]!.GetValue<string>();

        using var download = await _downstreamClient.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(zip, await download.Content.ReadAsByteArrayAsync());

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("v1.0.0", stored.Version);
        Assert.Equal(Package, stored.Artifact.Name);
        Assert.Equal($"http://localhost/feeds/{UpstreamFeed}/composer", stored.CachedFrom);
        Assert.Equal("A test package", stored.Description);
        Assert.True(stored.HasReadme);
        Assert.Equal(1, stored.Downloads);

        // The stored version is listed as the mirror listed it, so lock files stay valid.
        Assert.Equal(listed, (await _downstreamClient.GetVersionsAsync(MirrorFeed, Package)).Single()!.ToJsonString());

        Assert.Contains("From upstream", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/artifacts/{Uri.EscapeDataString(Package)}"));
    }

    [Fact]
    public async Task DevelopmentVersionsArePassedThroughWithoutStoringThem()
    {
        await SetUpAsync();
        await UploadUpstreamAsync("dev-main");

        var version = Assert.Single(await _downstreamClient.GetVersionsAsync(MirrorFeed, Package, dev: true))!;
        using var download = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/composer/dists/{Package}/dev-main/x.zip");

        Assert.StartsWith($"http://localhost/feeds/{UpstreamFeed}/composer/dists/", version["dist"]!["url"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task VersionsUploadedToTheMirrorComeFirst()
    {
        await SetUpAsync();
        await UploadUpstreamAsync("1.0.0");
        await UploadUpstreamAsync("1.1.0");

        var local = ComposerZipBuilder.Create(Package, readme: "local");
        using (var upload = await _downstreamClient.UploadPackageAsync(MirrorFeed, Package, "v1.1.0", local))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        var versions = await _downstreamClient.GetVersionsAsync(MirrorFeed, Package);

        Assert.Equal(new[] { "v1.1.0", "1.0.0" }, versions.Select(v => v!["version"]!.GetValue<string>()));
        Assert.Equal(local, await _downstreamClient.GetByteArrayAsync($"feeds/{MirrorFeed}/composer/dists/{Package}/1.1.0/x.zip"));
    }

    [Theory]
    [InlineData("composer/p2/acme/unknown.json")]
    [InlineData("composer/p2/acme/unknown~dev.json")]
    [InlineData("composer/dists/acme/unknown/1.0.0/x.zip")]
    public async Task UnknownPackageIsNotFound(string path)
    {
        await SetUpAsync();

        using var response = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/{path}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task SetUpAsync()
    {
        await _upstream.CreateComposerFeedAsync(UpstreamFeed);
        await _downstream.CreateComposerFeedAsync(MirrorFeed, mirrorSource: $"http://localhost/feeds/{UpstreamFeed}/composer");
    }

    private async Task UploadUpstreamAsync(string version, byte[] zip = null)
    {
        using var response = await _upstreamClient.UploadPackageAsync(UpstreamFeed, Package, version, zip);
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
