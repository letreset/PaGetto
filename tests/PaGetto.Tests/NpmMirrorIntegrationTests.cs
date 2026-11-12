using System;
using System.Linq;
using System.Net;
using System.Net.Http;
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
/// An npm feed that mirrors another npm registry. The upstream is an npm feed of a second PaGetto
/// instance, so the mirror client is tested against a real registry API.
/// </summary>
public class NpmMirrorIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "npm";
    private const string MirrorFeed = "proxy";

    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public NpmMirrorIntegrationTests(ITestOutputHelper output)
    {
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler());

        _upstreamClient = _upstream.CreateClient();
        _downstreamClient = _downstream.CreateClient();
    }

    [Fact]
    public async Task ListsUpstreamVersionsWithTarballsServedByTheMirror()
    {
        await SetUpAsync();
        using (await _upstreamClient.PublishAsync(UpstreamFeed, "@scope/pkg", "1.0.0"))
        using (await _upstreamClient.PublishAsync(UpstreamFeed, "@scope/pkg", "2.0.0-rc.1", tag: "next"))
        {
        }

        var packument = await _downstreamClient.GetJsonObjectAsync(NpmTestClient.PackageUrl(MirrorFeed, "@scope/pkg"));

        Assert.Equal(new[] { "1.0.0", "2.0.0-rc.1" }, packument["versions"]!.AsObject().Select(v => v.Key));
        Assert.Equal("1.0.0", packument["dist-tags"]?["latest"]?.GetValue<string>());
        Assert.Equal("2.0.0-rc.1", packument["dist-tags"]?["next"]?.GetValue<string>());
        Assert.Equal(
            $"http://localhost/feeds/{MirrorFeed}/npm/@scope/pkg/-/pkg-1.0.0.tgz",
            packument["versions"]!["1.0.0"]!["dist"]!["tarball"]?.GetValue<string>());

        // Listing a package doesn't store it.
        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task StoresAVersionOnItsFirstDownload()
    {
        await SetUpAsync();
        var tarball = NpmTarballBuilder.Create("@scope/pkg", "1.0.0", readme: "# From upstream");
        using (await _upstreamClient.PutJsonAsync(
            NpmTestClient.PackageUrl(UpstreamFeed, "@scope/pkg"),
            NpmTestClient.CreatePublishBody("@scope/pkg", "1.0.0", tarball)))
        {
        }

        using var download = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/npm/@scope/pkg/-/pkg-1.0.0.tgz");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(tarball, await download.Content.ReadAsByteArrayAsync());

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("1.0.0", stored.Version);
        Assert.Equal($"http://localhost/feeds/{UpstreamFeed}/npm", stored.CachedFrom);
        Assert.True(stored.HasReadme);
        Assert.Equal(1, stored.Downloads);

        // The stored version is served as the feed's own, with the same integrity.
        var upstream = await _upstreamClient.GetJsonObjectAsync(NpmTestClient.PackageUrl(UpstreamFeed, "@scope/pkg"));
        var mirrored = await _downstreamClient.GetJsonObjectAsync(NpmTestClient.PackageUrl(MirrorFeed, "@scope/pkg") + "?write=true");
        Assert.Equal(
            upstream["versions"]!["1.0.0"]!["dist"]!["integrity"]?.GetValue<string>(),
            mirrored["versions"]!["1.0.0"]!["dist"]!["integrity"]?.GetValue<string>());

        // The mirrored package now shows up in the web UI of the mirror.
        Assert.Contains("@scope/pkg", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/"));
        Assert.Contains("From upstream", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/artifacts/%40scope%2Fpkg"));
    }

    [Fact]
    public async Task VersionsPublishedToTheMirrorComeFirst()
    {
        await SetUpAsync();
        using (await _upstreamClient.PublishAsync(UpstreamFeed, "my-package", "1.0.0"))
        using (await _upstreamClient.PublishAsync(UpstreamFeed, "my-package", "2.0.0"))
        using (await _downstreamClient.PublishAsync(MirrorFeed, "my-package", "1.5.0"))
        {
        }

        var url = NpmTestClient.PackageUrl(MirrorFeed, "my-package");
        var packument = await _downstreamClient.GetJsonObjectAsync(url);

        Assert.Equal(new[] { "1.0.0", "1.5.0", "2.0.0" }, packument["versions"]!.AsObject().Select(v => v.Key));

        // The feed's own latest tag wins over the upstream's.
        Assert.Equal("1.5.0", packument["dist-tags"]?["latest"]?.GetValue<string>());

        // Clients that change the package only get the feed's own versions.
        var forWriting = await _downstreamClient.GetJsonObjectAsync(url + "?write=true");
        Assert.Equal(new[] { "1.5.0" }, forWriting["versions"]!.AsObject().Select(v => v.Key));
    }

    [Fact]
    public async Task KeepsTheDeprecationOfAnUpstreamVersion()
    {
        await SetUpAsync();
        using (await _upstreamClient.PublishAsync(UpstreamFeed, "my-package", "1.0.0"))
        {
        }

        var upstreamUrl = NpmTestClient.PackageUrl(UpstreamFeed, "my-package");
        var document = await _upstreamClient.GetJsonObjectAsync(upstreamUrl + "?write=true");
        document["versions"]!["1.0.0"]!["deprecated"] = "Gone";
        using (await _upstreamClient.PutJsonAsync(upstreamUrl, document))
        using (await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/npm/my-package/-/my-package-1.0.0.tgz"))
        {
        }

        Assert.False(Assert.Single(await GetStoredVersionsAsync()).Listed);

        var packument = await _downstreamClient.GetJsonObjectAsync(NpmTestClient.PackageUrl(MirrorFeed, "my-package"));
        Assert.Equal("Gone", packument["versions"]!["1.0.0"]!["deprecated"]?.GetValue<string>());
    }

    [Fact]
    public async Task UnknownPackageIsNotFound()
    {
        await SetUpAsync();

        using var listing = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/npm/unknown");
        using var download = await _downstreamClient.GetAsync($"feeds/{MirrorFeed}/npm/unknown/-/unknown-1.0.0.tgz");

        Assert.Equal(HttpStatusCode.NotFound, listing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    private async Task SetUpAsync()
    {
        await _upstream.CreateNpmFeedAsync(UpstreamFeed);
        await _downstream.CreateNpmFeedAsync(MirrorFeed, mirrorSource: $"http://localhost/feeds/{UpstreamFeed}/npm");
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
