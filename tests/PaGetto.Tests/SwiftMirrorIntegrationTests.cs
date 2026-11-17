using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Swift;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// A Swift feed that mirrors another registry. The upstream is a Swift feed of a second PaGetto
/// instance, so the mirror client is tested against a real Swift package registry.
/// </summary>
public class SwiftMirrorIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "swift";
    private const string MirrorFeed = "proxy";
    private const string Package = "mona.LinkedList";

    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public SwiftMirrorIntegrationTests(ITestOutputHelper output)
    {
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler());

        _upstreamClient = _upstream.CreateClient();
        _downstreamClient = _downstream.CreateClient();
    }

    [Fact]
    public async Task ListsUpstreamReleasesWithoutStoringThem()
    {
        await SetUpAsync();
        await PublishUpstreamAsync("1.0.0");
        await PublishUpstreamAsync("1.1.0");

        Assert.Equal(new[] { "1.1.0", "1.0.0" }, await _downstreamClient.GetReleaseVersionsAsync(MirrorFeed, Package));
        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task StoresAReleaseOnItsFirstDownload()
    {
        await SetUpAsync();
        var archive = SwiftTestClient.SourceArchive(readme: "# LinkedList", extraFiles: new Dictionary<string, string>
        {
            ["Package@swift-6.swift"] = SwiftTestClient.PackageSwift(toolsVersion: "6.0"),
        });
        var signature = RandomNumberGenerator.GetBytes(32);
        await PublishUpstreamAsync("1.0.0", archive, SwiftTestClient.Metadata(repositoryUrls: "https://github.com/mona/LinkedList"), signature);
        var upstream = await _upstreamClient.GetReleaseAsync(UpstreamFeed, Package, "1.0.0");

        // Swift Package Manager asks for the release first, so that stores it.
        var release = await _downstreamClient.GetReleaseAsync(MirrorFeed, Package, "1.0.0");

        Assert.Equal(upstream.ToJsonString(), release.ToJsonString());
        Assert.Equal(archive, await _downstreamClient.GetByteArrayAsync(SwiftTestClient.ReleaseUrl(MirrorFeed, Package, "1.0.0") + ".zip"));
        Assert.Equal(
            SwiftTestClient.PackageSwift(toolsVersion: "6.0"),
            await _downstreamClient.GetStringAsync(SwiftTestClient.ReleaseUrl(MirrorFeed, Package, "1.0.0") + "/Package.swift?swift-version=6.0.0"));

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("mona.linkedlist", stored.Artifact.Name);
        Assert.Equal($"http://localhost/feeds/{UpstreamFeed}/swift", stored.CachedFrom);
        Assert.Equal("A linked list", stored.Description);
        Assert.True(stored.HasReadme);
        Assert.Equal(Convert.ToBase64String(signature), SwiftManifest.Parse(stored.MetadataJson).Signature);

        // The stored release is served from the feed from now on.
        Assert.Contains("<h1>mona.LinkedList</h1>", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/artifacts/mona.LinkedList"));
        Assert.Equal(
            """{"identifiers":["mona.LinkedList"]}""",
            await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/swift/identifiers?url=https://github.com/mona/LinkedList"));
    }

    [Fact]
    public async Task IdentifiersAreLookedUpOnTheMirror()
    {
        await SetUpAsync();
        await PublishUpstreamAsync("1.0.0", metadata: SwiftTestClient.Metadata(repositoryUrls: "https://github.com/mona/LinkedList"));

        var found = await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/swift/identifiers?url=https://github.com/mona/LinkedList.git");

        Assert.Equal("""{"identifiers":["mona.LinkedList"]}""", found);
        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task ReleasesPublishedToTheFeedComeFirst()
    {
        await SetUpAsync();
        await PublishUpstreamAsync("1.0.0");
        await PublishUpstreamAsync("1.1.0");

        var local = SwiftTestClient.SourceArchive(readme: "local");
        using (var publish = await _downstreamClient.PublishAsync(MirrorFeed, Package, "1.1.0", local))
        {
            Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        }

        using (var publish = await _downstreamClient.PublishAsync(MirrorFeed, Package, "2.0.0", SwiftTestClient.SourceArchive()))
        {
            Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        }

        Assert.Equal(new[] { "2.0.0", "1.1.0", "1.0.0" }, await _downstreamClient.GetReleaseVersionsAsync(MirrorFeed, Package));
        Assert.Equal(local, await _downstreamClient.GetByteArrayAsync(SwiftTestClient.ReleaseUrl(MirrorFeed, Package, "1.1.0") + ".zip"));
    }

    [Fact]
    public async Task UnlistedReleaseOfTheFeedHidesTheMirrorsRelease()
    {
        await SetUpAsync();
        await PublishUpstreamAsync("1.0.0");
        await PublishUpstreamAsync("1.1.0");
        await _downstreamClient.GetReleaseAsync(MirrorFeed, Package, "1.1.0");

        using (var scope = _downstream.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            var stored = await context.ArtifactVersions.SingleAsync();
            stored.Listed = false;
            await context.SaveChangesAsync(default);
        }

        Assert.Equal(new[] { "1.0.0" }, await _downstreamClient.GetReleaseVersionsAsync(MirrorFeed, Package));
    }

    [Theory]
    [InlineData("mona.Unknown", "1.0.0")]
    [InlineData(Package, "9.9.9")]
    public async Task UnknownPackageOrReleaseIsNotFound(string identifier, string version)
    {
        await SetUpAsync();
        await PublishUpstreamAsync("1.0.0");

        using var response = await _downstreamClient.GetAsync(SwiftTestClient.ReleaseUrl(MirrorFeed, identifier, version));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await GetStoredVersionsAsync());
    }

    private async Task SetUpAsync()
    {
        await _upstream.CreateSwiftFeedAsync(UpstreamFeed);
        await _downstream.CreateSwiftFeedAsync(MirrorFeed, mirrorSource: $"http://localhost/feeds/{UpstreamFeed}/swift");
    }

    private async Task PublishUpstreamAsync(string version, byte[] archive = null, string metadata = null, byte[] signature = null)
    {
        using var response = await _upstreamClient.PublishAsync(
            UpstreamFeed, Package, version, archive ?? SwiftTestClient.SourceArchive(), metadata ?? SwiftTestClient.Metadata(),
            signature, signature == null ? null : "cms-1.0.0");
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
