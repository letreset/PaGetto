using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Ivy;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// An Ivy feed that mirrors another Ivy repository. The upstream is an Ivy feed of a second PaGetto
/// instance, so the mirror client is tested against a real repository with its directory listings;
/// a test of what such a feed can't serve (a wrong checksum) uses a fixed upstream instead.
/// </summary>
public class IvyMirrorIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "plugins";
    private const string MirrorFeed = "proxy";

    private readonly ITestOutputHelper _output;
    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public IvyMirrorIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler());

        _upstreamClient = _upstream.CreateClient();
        _downstreamClient = _downstream.CreateClient();
    }

    [Fact]
    public async Task TheListingHasTheRevisionsOfTheMirrorAndTheFeed()
    {
        await SetUpAsync();
        await _upstreamClient.PublishPluginAsync(UpstreamFeed, "sbt-tool", "1.0.0");
        await _upstreamClient.PublishPluginAsync(UpstreamFeed, "sbt-tool", "2.0.0");
        await _downstreamClient.PublishPluginAsync(MirrorFeed, "sbt-tool", "1.5.0");

        var listingUrl = IvyTestClient.Url(MirrorFeed, "com.example/sbt-tool/scala_2.12/sbt_1.0/");
        var html = await _downstreamClient.GetStringAsync(listingUrl);

        Assert.Equal(new[] { "1.0.0", "1.5.0", "2.0.0" }, IvyUpstreamClient.ParseFolders(new Uri($"http://localhost/{listingUrl}"), html));

        // Listing doesn't store anything.
        Assert.Equal(new[] { "1.5.0" }, (await GetStoredVersionsAsync()).Select(v => v.Version));
    }

    [Fact]
    public async Task StoresAFileOnItsFirstDownload()
    {
        await SetUpAsync();
        var jar = await _upstreamClient.PublishPluginAsync(UpstreamFeed, "sbt-tool", "1.0.0", description: "From upstream.");

        var descriptor = await _downstreamClient.GetStringAsync(IvyTestClient.PluginFileUrl(MirrorFeed, "sbt-tool", "1.0.0", IvyPath.DescriptorFileName));
        var downloaded = await _downstreamClient.GetByteArrayAsync(IvyTestClient.PluginFileUrl(MirrorFeed, "sbt-tool", "1.0.0", "jars/sbt-tool.jar"));
        var sha1 = await _downstreamClient.GetStringAsync(IvyTestClient.PluginFileUrl(MirrorFeed, "sbt-tool", "1.0.0", "jars/sbt-tool.jar.sha1"));

        Assert.Contains("module=\"sbt-tool\"", descriptor);
        Assert.Equal(jar, downloaded);
        Assert.Equal(MavenTestClient.Checksums(jar)["sha1"], sha1);

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("1.0.0", stored.Version);
        Assert.Equal($"http://localhost/feeds/{UpstreamFeed}/ivy", stored.CachedFrom);
        Assert.Equal("From upstream.", stored.Description);
        Assert.Equal(1, stored.Downloads);
        Assert.Equal(new[] { "ivys/ivy.xml", "jars/sbt-tool.jar" }, stored.Files.Select(f => f.FileName).Order());

        // The mirrored module now shows up in the web UI of the mirror.
        Assert.Contains("com.example:sbt-tool:scala_2.12:sbt_1.0", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/"));
    }

    [Fact]
    public async Task ARevisionPublishedToTheFeedHidesTheMirror()
    {
        await SetUpAsync();
        await _upstreamClient.PublishPluginAsync(UpstreamFeed, "sbt-tool", "1.0.0");
        await _downstreamClient.PutFileAsync(
            IvyTestClient.PluginFileUrl(MirrorFeed, "sbt-tool", "1.0.0", "srcs/sbt-tool-sources.jar"),
            MavenTestClient.Jar("sources"));

        using var jar = await _downstreamClient.GetAsync(IvyTestClient.PluginFileUrl(MirrorFeed, "sbt-tool", "1.0.0", "jars/sbt-tool.jar"));

        Assert.Equal(HttpStatusCode.NotFound, jar.StatusCode);
    }

    [Fact]
    public async Task SnapshotsAreNotMirrored()
    {
        await SetUpAsync();
        await _upstreamClient.PublishPluginAsync(UpstreamFeed, "sbt-tool", "1.0.0-SNAPSHOT");

        using var jar = await _downstreamClient.GetAsync(IvyTestClient.PluginFileUrl(MirrorFeed, "sbt-tool", "1.0.0-SNAPSHOT", "jars/sbt-tool.jar"));

        Assert.Equal(HttpStatusCode.NotFound, jar.StatusCode);
    }

    [Fact]
    public async Task AFileThatDoesNotMatchTheMirrorsChecksumIsNotStored()
    {
        using var upstream = new FixedUpstreamHandler(new Dictionary<string, string>
        {
            ["/ivy/com.example/sbt-tool/1.0.0/jars/sbt-tool.jar"] = "the jar",
            ["/ivy/com.example/sbt-tool/1.0.0/jars/sbt-tool.jar.sha1"] = "0000000000000000000000000000000000000000",
        });
        using var app = new PaGettoApplication(_output, upstream);
        using var client = app.CreateClient();
        await app.CreateIvyFeedAsync(MirrorFeed, mirrorSource: "https://upstream.test/ivy");

        using var response = await client.GetAsync(IvyTestClient.Url(MirrorFeed, "com.example/sbt-tool/1.0.0/jars/sbt-tool.jar"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await GetStoredVersionsAsync(app));
    }

    [Fact]
    public async Task UnknownModuleIsNotFound()
    {
        await SetUpAsync();

        using var listing = await _downstreamClient.GetAsync(IvyTestClient.Url(MirrorFeed, "com.example/unknown/scala_2.12/sbt_1.0/"));
        using var jar = await _downstreamClient.GetAsync(IvyTestClient.PluginFileUrl(MirrorFeed, "unknown", "1.0.0", "jars/unknown.jar"));

        Assert.Equal(HttpStatusCode.NotFound, listing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, jar.StatusCode);
    }

    private async Task SetUpAsync()
    {
        await _upstream.CreateIvyFeedAsync(UpstreamFeed);
        await _downstream.CreateIvyFeedAsync(MirrorFeed, mirrorSource: $"http://localhost/feeds/{UpstreamFeed}/ivy");
    }

    private Task<ArtifactVersion[]> GetStoredVersionsAsync()
    {
        return GetStoredVersionsAsync(_downstream);
    }

    private static async Task<ArtifactVersion[]> GetStoredVersionsAsync(PaGettoApplication app)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return await context.ArtifactVersions.AsNoTracking().Include(v => v.Files).ToArrayAsync();
    }

    public void Dispose()
    {
        _upstreamClient.Dispose();
        _downstreamClient.Dispose();
        _upstream.Dispose();
        _downstream.Dispose();
    }

    /// <summary>
    /// An upstream that serves fixed text by path, and 404 for anything else.
    /// </summary>
    private sealed class FixedUpstreamHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, string> _files;

        public FixedUpstreamHandler(IReadOnlyDictionary<string, string> files)
        {
            _files = files;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _files.TryGetValue(request.RequestUri!.AbsolutePath, out var content)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);

            return Task.FromResult(response);
        }
    }
}
