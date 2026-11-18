using System;
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
/// A VSIX feed that mirrors another gallery. The upstream is a VSIX feed of a second PaGetto
/// instance, so the mirror client is tested against a real gallery feed.
/// </summary>
public class VsixMirrorIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "vs";
    private const string MirrorFeed = "proxy";
    private const string Id = "Contoso.HelloTools";

    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public VsixMirrorIntegrationTests(ITestOutputHelper output)
    {
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler());

        _upstreamClient = _upstream.CreateClient();
        _downstreamClient = _downstream.CreateClient();
    }

    [Fact]
    public async Task GalleryListsUpstreamExtensionsWithoutStoringThem()
    {
        await SetUpAsync();
        await UploadUpstreamAsync("1.0");

        var entry = (await _downstreamClient.GetVsixGalleryAsync(MirrorFeed))[Id];

        Assert.Equal("1.0", entry.Manifest.Version);
        Assert.Equal("Hello Tools", entry.Manifest.DisplayName);
        Assert.Equal(2, entry.Manifest.InstallationTargets.Count);

        // Linked on this feed, so Visual Studio needs no access to the mirror.
        Assert.Equal($"http://localhost/feeds/{MirrorFeed}/vsix/extensions/{Id}/1.0/{Id}-1.0.vsix", entry.DownloadUrl);
        Assert.Equal($"http://localhost/feeds/{MirrorFeed}/vsix/extensions/{Id}/1.0/icon", entry.IconUrl);
        Assert.Empty(await GetStoredVersionsAsync());

        // The images are passed through.
        Assert.Equal(VsixPackageBuilder.Icon, await _downstreamClient.GetByteArrayAsync(entry.IconUrl));
        Assert.Equal(VsixPackageBuilder.PreviewImage, await _downstreamClient.GetByteArrayAsync(entry.PreviewImageUrl));
        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task StoresAVersionOnItsFirstDownload()
    {
        await SetUpAsync();
        var vsix = VsixPackageBuilder.Create(Id, "1.0");
        await UploadUpstreamAsync("1.0", vsix);
        var entry = (await _downstreamClient.GetVsixGalleryAsync(MirrorFeed))[Id];

        using var download = await _downstreamClient.GetAsync(entry.DownloadUrl);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(vsix, await download.Content.ReadAsByteArrayAsync());

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("1.0", stored.Version);
        Assert.Equal("contoso.hellotools", stored.Artifact.Name);
        Assert.Equal($"http://localhost/feeds/{UpstreamFeed}/vsix/atom.xml", stored.CachedFrom);
        Assert.Equal(1, stored.Downloads);

        // The stored version is offered as the mirror offered it, with its images from the stored file.
        var again = (await _downstreamClient.GetVsixGalleryAsync(MirrorFeed))[Id];
        Assert.Equal(entry.DownloadUrl, again.DownloadUrl);
        Assert.Equal(VsixPackageBuilder.Icon, await _downstreamClient.GetByteArrayAsync(again.IconUrl));
        Assert.Contains("Hello Tools", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/artifacts/contoso.hellotools"));
    }

    [Fact]
    public async Task VersionsUploadedToTheFeedComeFirst()
    {
        await SetUpAsync();
        await UploadUpstreamAsync("1.1");

        var local = VsixPackageBuilder.Create(Id, "1.1", VsixPackageBuilder.Manifest(Id, "1.1").Replace("Hello Tools", "Local Tools"));
        using (var upload = await _downstreamClient.UploadVsixAsync(MirrorFeed, Id, "1.1", local))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        var entry = (await _downstreamClient.GetVsixGalleryAsync(MirrorFeed))[Id];

        Assert.Equal("Local Tools", entry.Manifest.DisplayName);
        Assert.Equal(local, await _downstreamClient.GetByteArrayAsync(entry.DownloadUrl));
    }

    [Fact]
    public async Task NewerVersionOfTheMirrorIsOfferedOverAnOlderOneOfTheFeed()
    {
        await SetUpAsync();
        await UploadUpstreamAsync("2.0");
        using (await _downstreamClient.UploadVsixAsync(MirrorFeed, Id, "1.0"))
        {
        }

        var entry = (await _downstreamClient.GetVsixGalleryAsync(MirrorFeed))[Id];
        using var older = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/vsix/extensions/{Id}/1.0/{Id}-1.0.vsix");

        Assert.Equal("2.0", entry.Manifest.Version);
        Assert.Equal(HttpStatusCode.OK, older.StatusCode);
    }

    [Theory]
    [InlineData("vsix/extensions/Unknown/1.0/Unknown-1.0.vsix")]
    [InlineData("vsix/extensions/Unknown/1.0/icon")]
    [InlineData("vsix/extensions/Contoso.HelloTools/0.9/Contoso.HelloTools-0.9.vsix")]
    [InlineData("vsix/extensions/Contoso.HelloTools/0.9/icon")]
    public async Task VersionsTheMirrorDoesNotOfferAreNotFound(string path)
    {
        await SetUpAsync();

        // A gallery offers one version of an extension, so the mirror's older versions are not reachable.
        await UploadUpstreamAsync("0.9");
        await UploadUpstreamAsync("1.0");

        using var response = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/{path}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task SetUpAsync()
    {
        await _upstream.CreateVsixFeedAsync(UpstreamFeed);
        await _downstream.CreateVsixFeedAsync(MirrorFeed, mirrorSource: $"http://localhost/feeds/{UpstreamFeed}/vsix/atom.xml");
    }

    private async Task UploadUpstreamAsync(string version, byte[] vsix = null)
    {
        using var response = await _upstreamClient.UploadVsixAsync(UpstreamFeed, Id, version, vsix);
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
