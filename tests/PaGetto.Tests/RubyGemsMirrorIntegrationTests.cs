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
/// A RubyGems feed that mirrors another gem server. The upstream is a RubyGems feed of a second
/// PaGetto instance, so the mirror client is tested against a real compact index.
/// </summary>
public class RubyGemsMirrorIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "rubygems";
    private const string MirrorFeed = "proxy";

    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public RubyGemsMirrorIntegrationTests(ITestOutputHelper output)
    {
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler());

        _upstreamClient = _upstream.CreateClient();
        _downstreamClient = _downstream.CreateClient();
    }

    [Fact]
    public async Task ServesUpstreamGemsWithoutStoringThem()
    {
        await SetUpAsync();
        await PushUpstreamAsync(RubyGemBuilder.Create("my_gem", "1.0.0"));

        var upstreamInfo = await _upstreamClient.GetStringAsync(RubyGemsTestClient.SourceUrl(UpstreamFeed) + "info/my_gem");
        Assert.Equal(upstreamInfo, await _downstreamClient.GetStringAsync(RubyGemsTestClient.SourceUrl(MirrorFeed) + "info/my_gem"));

        // Bundler only looks up gems that /versions lists.
        var upstreamLine = Assert.Single(await _upstreamClient.GetIndexLinesAsync(RubyGemsTestClient.SourceUrl(UpstreamFeed) + "versions"));
        Assert.Equal(upstreamLine, Assert.Single(await _downstreamClient.GetIndexLinesAsync(RubyGemsTestClient.SourceUrl(MirrorFeed) + "versions")));

        var quickSpec = "quick/Marshal.4.8/my_gem-1.0.0.gemspec.rz";
        Assert.Equal(
            await _upstreamClient.GetByteArrayAsync(RubyGemsTestClient.SourceUrl(UpstreamFeed) + quickSpec),
            await _downstreamClient.GetByteArrayAsync(RubyGemsTestClient.SourceUrl(MirrorFeed) + quickSpec));

        // The mirror's gems are not listed in /names.
        Assert.Equal("---\n", await _downstreamClient.GetStringAsync(RubyGemsTestClient.SourceUrl(MirrorFeed) + "names"));
        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task StoresAGemOnItsFirstDownload()
    {
        await SetUpAsync();
        var gem = RubyGemBuilder.Create("my_gem", "1.0.0", "java", readme: "# From upstream");
        await PushUpstreamAsync(gem);

        using var download = await _downstreamClient.GetAsync(RubyGemsTestClient.SourceUrl(MirrorFeed) + "gems/my_gem-1.0.0-java.gem");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(gem, await download.Content.ReadAsByteArrayAsync());

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("1.0.0", stored.Version);
        Assert.Equal("my_gem", stored.Artifact.Name);
        Assert.Equal($"http://localhost/feeds/{UpstreamFeed}/rubygems/", stored.CachedFrom);
        Assert.True(stored.HasReadme);
        Assert.Equal(1, stored.Downloads);
        Assert.Equal("my_gem-1.0.0-java.gem", Assert.Single(stored.Files).FileName);

        // The stored gem is listed as the upstream lists it.
        Assert.Equal(
            await _upstreamClient.GetStringAsync(RubyGemsTestClient.SourceUrl(UpstreamFeed) + "info/my_gem"),
            await _downstreamClient.GetStringAsync(RubyGemsTestClient.SourceUrl(MirrorFeed) + "info/my_gem"));
        Assert.Contains("From upstream", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/artifacts/my_gem"));
    }

    [Fact]
    public async Task GemsPushedToTheMirrorAreListedNextToUpstreamOnes()
    {
        await SetUpAsync();
        await PushUpstreamAsync(RubyGemBuilder.Create("my_gem", "1.0.0"));
        await PushUpstreamAsync(RubyGemBuilder.Create("my_gem", "1.1.0"));

        // A version pushed to the feed hides the same version of the mirror.
        using (var push = await _downstreamClient.PushGemAsync(MirrorFeed, RubyGemBuilder.Create("my_gem", "1.1.0", dependencies: [])))
        {
            Assert.Equal(HttpStatusCode.OK, push.StatusCode);
        }

        using (await _downstreamClient.PushGemAsync(MirrorFeed, "private_gem", "0.1.0"))
        {
        }

        var lines = await _downstreamClient.GetIndexLinesAsync(RubyGemsTestClient.SourceUrl(MirrorFeed) + "info/my_gem");
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("1.0.0 rake:~> 13.0|", lines[0]);
        Assert.StartsWith("1.1.0 |", lines[1]);

        // The feed's gems come first, then the mirror's.
        var versions = await _downstreamClient.GetIndexLinesAsync(RubyGemsTestClient.SourceUrl(MirrorFeed) + "versions");
        Assert.Equal(new[] { "my_gem 1.1.0", "private_gem 0.1.0", "my_gem 1.0.0,1.1.0" }, versions.Select(l => l[..l.LastIndexOf(' ')]));
    }

    [Fact]
    public async Task YankingAMirroredVersionHidesIt()
    {
        await SetUpAsync();
        await PushUpstreamAsync(RubyGemBuilder.Create("my_gem", "1.0.0"));
        await PushUpstreamAsync(RubyGemBuilder.Create("my_gem", "1.1.0"));
        using (await _downstreamClient.GetAsync(RubyGemsTestClient.SourceUrl(MirrorFeed) + "gems/my_gem-1.0.0.gem"))
        {
        }

        using var yank = await _downstreamClient.YankGemAsync(MirrorFeed, "my_gem", "1.0.0");

        Assert.Equal(HttpStatusCode.OK, yank.StatusCode);
        Assert.StartsWith("1.1.0 ", Assert.Single(await _downstreamClient.GetIndexLinesAsync(RubyGemsTestClient.SourceUrl(MirrorFeed) + "info/my_gem")));
        Assert.StartsWith("my_gem -1.0.0 ", (await _downstreamClient.GetIndexLinesAsync(RubyGemsTestClient.SourceUrl(MirrorFeed) + "versions"))[0]);
    }

    [Theory]
    [InlineData("info/unknown")]
    [InlineData("gems/unknown-1.0.0.gem")]
    [InlineData("quick/Marshal.4.8/unknown-1.0.0.gemspec.rz")]
    public async Task UnknownGemIsNotFound(string path)
    {
        await SetUpAsync();

        using var response = await _downstreamClient.GetAsync(RubyGemsTestClient.SourceUrl(MirrorFeed) + path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task SetUpAsync()
    {
        await _upstream.CreateRubyGemsFeedAsync(UpstreamFeed);
        await _downstream.CreateRubyGemsFeedAsync(MirrorFeed, mirrorSource: $"http://localhost/feeds/{UpstreamFeed}/rubygems/");
    }

    private async Task PushUpstreamAsync(byte[] gem)
    {
        using var response = await _upstreamClient.PushGemAsync(UpstreamFeed, gem);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<ArtifactVersion[]> GetStoredVersionsAsync()
    {
        using var scope = _downstream.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return await context.ArtifactVersions.AsNoTracking().Include(v => v.Artifact).Include(v => v.Files).ToArrayAsync();
    }

    public void Dispose()
    {
        _upstreamClient.Dispose();
        _downstreamClient.Dispose();
        _upstream.Dispose();
        _downstream.Dispose();
    }
}
