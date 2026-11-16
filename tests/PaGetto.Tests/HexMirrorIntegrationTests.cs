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
/// A Hex feed that mirrors another Hex repository. The upstream is a Hex feed of a second PaGetto
/// instance, so the mirror client is tested against a real signed registry.
/// </summary>
public class HexMirrorIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "upstream";
    private const string MirrorFeed = "proxy";

    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public HexMirrorIntegrationTests(ITestOutputHelper output)
    {
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler());

        _upstreamClient = _upstream.CreateClient();
        _downstreamClient = _downstream.CreateClient();
    }

    [Fact]
    public async Task ListsUpstreamVersionsSignedWithTheMirrorsKeyWithoutStoringThem()
    {
        await SetUpAsync();
        var tarball = HexTarballBuilder.Create("demo", "1.0.0");
        using (await _upstreamClient.PublishHexAsync(UpstreamFeed, "demo", tarball))
        {
        }

        var release = Assert.Single(await _downstreamClient.GetReleasesAsync(MirrorFeed, "demo"));

        Assert.Equal("1.0.0", release.Version);
        Assert.Equal(SHA256.HashData(tarball), release.OuterChecksum);
        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task StoresAVersionOnItsFirstDownload()
    {
        await SetUpAsync();

        // A dependency in the upstream's own repository is listed without one, so it stays in the mirror.
        var requirements = $"[{HexTarballBuilder.Requirement("other", "~> 1.0", repository: UpstreamFeed)}]";
        var tarball = HexTarballBuilder.Create("demo", "1.0.0", readme: "# From upstream", requirements: requirements);
        using (await _upstreamClient.PublishHexAsync(UpstreamFeed, "demo", tarball))
        {
        }

        using (await _upstreamClient.RetireHexAsync(UpstreamFeed, "demo", "1.0.0", "invalid", "Broken"))
        {
        }

        using var download = await _downstreamClient.GetAsync(HexTestClient.TarballUrl(MirrorFeed, "demo", "1.0.0"));

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(tarball, await download.Content.ReadAsByteArrayAsync());

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("1.0.0", stored.Version);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(tarball)), stored.ContentHash);
        Assert.Equal($"http://localhost/feeds/{UpstreamFeed}/hex", stored.CachedFrom);
        Assert.Equal("A test package.", stored.Description);
        Assert.True(stored.HasReadme);
        Assert.False(stored.Listed);
        Assert.Equal(1, stored.Downloads);

        // The stored version keeps the upstream's registry entry.
        var release = Assert.Single(await _downstreamClient.GetReleasesAsync(MirrorFeed, "demo"));
        Assert.Equal("invalid", release.Retirement.Reason);
        Assert.Equal("Broken", release.Retirement.Message);
        var requirement = Assert.Single(release.Requirements);
        Assert.Equal("other", requirement.Name);
        Assert.Null(requirement.Repository);

        Assert.Contains("From upstream", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/artifacts/demo"));
    }

    [Fact]
    public async Task VersionsPublishedToTheMirrorAreListedNextToUpstreamOnes()
    {
        await SetUpAsync();
        using (await _upstreamClient.PublishHexAsync(UpstreamFeed, "demo", "1.0.0"))
        {
        }

        using (await _downstreamClient.PublishHexAsync(MirrorFeed, "demo", "2.0.0"))
        {
        }

        var releases = await _downstreamClient.GetReleasesAsync(MirrorFeed, "demo");

        Assert.Equal(new[] { "1.0.0", "2.0.0" }, releases.Select(r => r.Version));
    }

    [Fact]
    public async Task UnknownPackageIsNotFound()
    {
        await SetUpAsync();

        using var registry = await _downstreamClient.GetAsync($"{HexTestClient.RepositoryUrl(MirrorFeed)}/packages/unknown");
        using var download = await _downstreamClient.GetAsync(HexTestClient.TarballUrl(MirrorFeed, "unknown", "1.0.0"));

        Assert.Equal(HttpStatusCode.NotFound, registry.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    private async Task SetUpAsync()
    {
        await _upstream.CreateHexFeedAsync(UpstreamFeed);
        await _downstream.CreateHexFeedAsync(MirrorFeed, mirrorSource: $"http://localhost/feeds/{UpstreamFeed}/hex");
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
