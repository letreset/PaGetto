using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Tests.Support;
using PaGetto.Core.VsCode;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// A VS Code feed that mirrors another gallery. The upstream is a VS Code feed of a second PaGetto
/// instance, so the mirror client is tested against a real gallery API.
/// </summary>
public class VsCodeMirrorIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "code";
    private const string MirrorFeed = "proxy";
    private const string Id = "Contoso.hello-world";

    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public VsCodeMirrorIntegrationTests(ITestOutputHelper output)
    {
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler());

        _upstreamClient = _upstream.CreateClient();
        _downstreamClient = _downstream.CreateClient();
    }

    [Fact]
    public async Task OffersTheMirrorsVersionsWithoutStoringThem()
    {
        await SetUpAsync();
        await UploadUpstreamAsync("1.0.0");
        await UploadUpstreamAsync("1.1.0", "linux-x64");

        var extension = await _downstreamClient.FindVsCodeExtensionAsync(MirrorFeed, Id);

        Assert.Equal("Hello World", extension.DisplayName);
        Assert.Equal([("1.1.0", "linux-x64"), ("1.0.0", "universal")], extension.Versions.Select(v => (v.Version, v.TargetPlatform)));

        // Linked on this feed, so the editor needs no access to the mirror.
        Assert.All(extension.Versions, v => Assert.StartsWith($"http://localhost/feeds/{MirrorFeed}/vscode/asset/Contoso/hello-world/", v.AssetUri));
        Assert.Empty(await GetStoredVersionsAsync());

        // The other assets are passed through.
        Assert.Equal(VsCodePackageBuilder.Readme, await _downstreamClient.GetStringAsync(extension.Versions[0].GetAssetUrl(VsCodeGalleryApi.DetailsAsset)));
        Assert.Equal(VsCodePackageBuilder.Icon, await _downstreamClient.GetByteArrayAsync(extension.Versions[1].GetAssetUrl(VsCodeGalleryApi.IconAsset)));
        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task StoresAFileOnItsFirstDownload()
    {
        await SetUpAsync();
        var vsix = await UploadUpstreamAsync("1.0.0", "linux-x64");
        var version = Assert.Single((await _downstreamClient.FindVsCodeExtensionAsync(MirrorFeed, Id)).Versions);

        using var download = await _downstreamClient.GetAsync(version.GetAssetUrl(VsCodeGalleryApi.VsixPackageAsset));

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(vsix, await download.Content.ReadAsByteArrayAsync());

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("1.0.0", stored.Version);
        Assert.Equal("contoso.hello-world", stored.Artifact.Name);
        Assert.Equal($"http://localhost/feeds/{UpstreamFeed}/vscode/gallery", stored.CachedFrom);
        Assert.Equal(1, stored.Downloads);
        Assert.Equal("Contoso.hello-world-1.0.0@linux-x64.vsix", Assert.Single(stored.Files).FileName);

        // The stored file is served from this feed, with its assets.
        var again = Assert.Single((await _downstreamClient.FindVsCodeExtensionAsync(MirrorFeed, Id)).Versions);
        Assert.Equal(version.AssetUri, again.AssetUri);
        Assert.Equal(vsix, await _downstreamClient.GetByteArrayAsync(again.GetAssetUrl(VsCodeGalleryApi.VsixPackageAsset)));
        Assert.Equal(VsCodePackageBuilder.Icon, await _downstreamClient.GetByteArrayAsync(again.GetAssetUrl(VsCodeGalleryApi.IconAsset)));
    }

    [Fact]
    public async Task PromotedMirroredVersionIsTheTargetFeedsOwn()
    {
        await SetUpAsync();
        var vsix = await UploadUpstreamAsync("1.0.0");
        var version = Assert.Single((await _downstreamClient.FindVsCodeExtensionAsync(MirrorFeed, Id)).Versions);
        Assert.Equal(vsix, await _downstreamClient.GetByteArrayAsync(version.GetAssetUrl(VsCodeGalleryApi.VsixPackageAsset)));
        await _downstream.CreateVsCodeFeedAsync("release");

        using var promote = await _downstreamClient.PostAsJsonAsync(
            $"feeds/{MirrorFeed}/api/v1/promote",
            new { packageId = "contoso.hello-world", version = "1.0.0", targetFeed = "release", move = false });
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);

        // Copied as published to the target, so it is offered as one of its own extensions.
        var promoted = Assert.Single((await _downstreamClient.FindVsCodeExtensionAsync("release", Id)).Versions);
        Assert.Contains(promoted.Properties, p => p.Key == VsCodeGalleryApi.PrivateProperty && p.Value == "true");
        Assert.Equal(vsix, await _downstreamClient.GetByteArrayAsync(promoted.GetAssetUrl(VsCodeGalleryApi.VsixPackageAsset)));
        Assert.Contains(await GetStoredVersionsAsync(), v => v.CachedFrom == null && v.Artifact.FeedId != Guid.Empty);
    }

    [Fact]
    public async Task FindsTheMirrorsExtensionsByTheirUuid()
    {
        await SetUpAsync();
        await UploadUpstreamAsync("1.0.0");
        var upstreamId = (await _upstreamClient.FindVsCodeExtensionAsync(UpstreamFeed, Id)).ExtensionId;

        // VS Code asks by name, then by the UUID of the answer for all versions.
        var byName = await _downstreamClient.FindVsCodeExtensionAsync(MirrorFeed, Id);
        var byUuid = await _downstreamClient.QueryVsCodeAsync(MirrorFeed, [(VsCodeGalleryApi.ExtensionIdFilter, byName.ExtensionId.ToString())]);

        Assert.Equal(upstreamId, byName.ExtensionId);
        Assert.Equal("hello-world", Assert.Single(byUuid.Extensions).ExtensionName);
        Assert.Empty((await _downstreamClient.QueryVsCodeAsync(MirrorFeed, [(VsCodeGalleryApi.ExtensionIdFilter, Guid.NewGuid().ToString())])).Extensions);
    }

    [Fact]
    public async Task SearchListsTheFeedsExtensionsAndThenTheMirrors()
    {
        await SetUpAsync();
        await UploadUpstreamAsync("1.0.0");
        using (var upload = await _downstreamClient.UploadVsCodeAsync(MirrorFeed, VsCodePackageBuilder.Create("Fabrikam", "linter", "1.0.0")))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        var all = await _downstreamClient.QueryVsCodeAsync(MirrorFeed, [(VsCodeGalleryApi.SearchTextFilter, "")]);
        var secondPage = await _downstreamClient.QueryVsCodeAsync(MirrorFeed, [], pageNumber: 2, pageSize: 1);

        Assert.Equal(2, all.GetTotalCount());
        Assert.Equal(["linter", "hello-world"], all.Extensions.Select(e => e.ExtensionName));
        Assert.StartsWith($"http://localhost/feeds/{MirrorFeed}/vscode/asset/", all.Extensions[1].Versions[0].AssetUri);
        Assert.Equal("hello-world", Assert.Single(secondPage.Extensions).ExtensionName);
    }

    [Fact]
    public async Task FeedsOwnExtensionsAreNotLookedUpOnTheMirror()
    {
        await SetUpAsync();
        await UploadUpstreamAsync("2.0.0");
        using (var upload = await _downstreamClient.UploadVsCodeAsync(MirrorFeed, VsCodePackageBuilder.Create("Contoso", "hello-world", "1.0.0")))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        var extension = await _downstreamClient.FindVsCodeExtensionAsync(MirrorFeed, Id);
        using var mirrored = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/vscode/asset/Contoso/hello-world/2.0.0/{VsCodeGalleryApi.VsixPackageAsset}");

        Assert.Equal(["1.0.0"], extension.Versions.Select(v => v.Version));
        Assert.Equal(HttpStatusCode.NotFound, mirrored.StatusCode);
    }

    private async Task SetUpAsync()
    {
        await _upstream.CreateVsCodeFeedAsync(UpstreamFeed);
        await _downstream.CreateVsCodeFeedAsync(MirrorFeed, $"http://localhost/feeds/{UpstreamFeed}/vscode/gallery");
    }

    private async Task<byte[]> UploadUpstreamAsync(string version, string targetPlatform = null)
    {
        var vsix = VsCodePackageBuilder.Create("Contoso", "hello-world", version, targetPlatform);
        using var upload = await _upstreamClient.UploadVsCodeAsync(UpstreamFeed, vsix);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        return vsix;
    }

    private async Task<ArtifactVersion[]> GetStoredVersionsAsync()
    {
        using var scope = _downstream.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return await context.ArtifactVersions
            .AsNoTracking()
            .Include(v => v.Artifact)
            .Include(v => v.Files)
            .ToArrayAsync();
    }

    public void Dispose()
    {
        _upstreamClient.Dispose();
        _downstreamClient.Dispose();
        _upstream.Dispose();
        _downstream.Dispose();
    }
}
