using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Cran;
using PaGetto.Core.Entities;
using PaGetto.Core.Tests.Support;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// A CRAN feed that mirrors another CRAN-like repository. The upstream is a CRAN feed of a second
/// PaGetto instance, so the mirror client is tested against a real repository with an archive.
/// </summary>
public class CranMirrorIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "r";
    private const string MirrorFeed = "proxy";
    private const string Package = "Hello";

    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public CranMirrorIntegrationTests(ITestOutputHelper output)
    {
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler());

        _upstreamClient = _upstream.CreateClient();
        _downstreamClient = _downstream.CreateClient();
    }

    [Fact]
    public async Task IndexListsUpstreamPackagesWithoutStoringThem()
    {
        await SetUpAsync();
        var package = CranPackageBuilder.Create(Package, "1.0");
        await UploadUpstreamAsync("1.0", package);

        var entry = (await _downstreamClient.GetCranIndexAsync(MirrorFeed))[Package];

        Assert.Equal("1.0", CranDcf.GetValue(entry, "Version"));
        Assert.Equal("rlang (>= 1.0.0), cli", CranDcf.GetValue(entry, "Imports"));
        Assert.Equal(Convert.ToHexStringLower(MD5.HashData(package)), CranDcf.GetValue(entry, "MD5sum"));
        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task StoresAVersionOnItsFirstDownload()
    {
        await SetUpAsync();
        var package = CranPackageBuilder.Create(Package, "1.0", readme: "# From upstream");
        await UploadUpstreamAsync("1.0", package);
        var listed = (await _downstreamClient.GetCranIndexAsync(MirrorFeed))[Package];

        using var download = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/cran/src/contrib/Hello_1.0.tar.gz");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(package, await download.Content.ReadAsByteArrayAsync());

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("1.0", stored.Version);
        Assert.Equal("hello", stored.Artifact.Name);
        Assert.Equal($"http://localhost/feeds/{UpstreamFeed}/cran", stored.CachedFrom);
        Assert.Equal("A Test Package", stored.Description);
        Assert.True(stored.HasReadme);
        Assert.Equal(1, stored.Downloads);

        // The stored version is listed as the mirror listed it.
        Assert.Equal(listed, (await _downstreamClient.GetCranIndexAsync(MirrorFeed))[Package]);

        Assert.Contains("From upstream", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/artifacts/hello"));
    }

    [Fact]
    public async Task OlderVersionsComeFromTheArchiveOfTheMirror()
    {
        await SetUpAsync();
        var old = CranPackageBuilder.Create(Package, "1.0", readme: "old");
        await UploadUpstreamAsync("1.0", old);
        await UploadUpstreamAsync("2.0");

        var listing = await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/cran/src/contrib/Archive/{Package}/");
        using var download = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/cran/src/contrib/Archive/{Package}/Hello_1.0.tar.gz");

        Assert.Contains("Hello_1.0.tar.gz", listing);
        Assert.DoesNotContain("Hello_2.0.tar.gz", listing);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(old, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("1.0", Assert.Single(await GetStoredVersionsAsync()).Version);
    }

    [Fact]
    public async Task VersionsUploadedToTheFeedComeFirst()
    {
        await SetUpAsync();
        await UploadUpstreamAsync("1.0");
        await UploadUpstreamAsync("1.1");

        var local = CranPackageBuilder.Create(Package, "1.1", readme: "local");
        using (var upload = await _downstreamClient.UploadCranPackageAsync(MirrorFeed, Package, "1.1", local))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        var entry = (await _downstreamClient.GetCranIndexAsync(MirrorFeed))[Package];

        Assert.Equal(Convert.ToHexStringLower(MD5.HashData(local)), CranDcf.GetValue(entry, "MD5sum"));
        Assert.Equal(local, await _downstreamClient.GetByteArrayAsync($"feeds/{MirrorFeed}/cran/src/contrib/Hello_1.1.tar.gz"));
    }

    [Fact]
    public async Task NewerVersionOfTheMirrorIsListedOverAnOlderOneOfTheFeed()
    {
        await SetUpAsync();
        await UploadUpstreamAsync("2.0");
        using (await _downstreamClient.UploadCranPackageAsync(MirrorFeed, Package, "1.0"))
        {
        }

        var entry = (await _downstreamClient.GetCranIndexAsync(MirrorFeed))[Package];
        var listing = await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/cran/src/contrib/Archive/{Package}/");

        Assert.Equal("2.0", CranDcf.GetValue(entry, "Version"));
        Assert.Contains("Hello_1.0.tar.gz", listing);
    }

    [Theory]
    [InlineData("cran/src/contrib/unknown_1.0.tar.gz")]
    [InlineData("cran/src/contrib/Archive/unknown/")]
    [InlineData("cran/src/contrib/Archive/unknown/unknown_1.0.tar.gz")]
    [InlineData("cran/src/contrib/Hello_9.9.tar.gz")]
    public async Task UnknownPackageIsNotFound(string path)
    {
        await SetUpAsync();
        await UploadUpstreamAsync("1.0");

        using var response = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/{path}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task SetUpAsync()
    {
        await _upstream.CreateCranFeedAsync(UpstreamFeed);
        await _downstream.CreateCranFeedAsync(MirrorFeed, mirrorSource: $"http://localhost/feeds/{UpstreamFeed}/cran");
    }

    private async Task UploadUpstreamAsync(string version, byte[] package = null)
    {
        using var response = await _upstreamClient.UploadCranPackageAsync(UpstreamFeed, Package, version, package);
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
