using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// A Maven feed that mirrors another Maven repository. The upstream is a Maven feed of a second
/// PaGetto instance, so the mirror client is tested against a real repository; tests of what such a
/// feed can't serve (a wrong checksum, plugin prefixes) use a fixed upstream instead.
/// </summary>
public class MavenMirrorIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "central";
    private const string MirrorFeed = "proxy";

    private readonly ITestOutputHelper _output;
    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public MavenMirrorIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler());

        _upstreamClient = _upstream.CreateClient();
        _downstreamClient = _downstream.CreateClient();
    }

    [Fact]
    public async Task TheMetadataListsTheVersionsOfTheMirrorAndTheFeed()
    {
        await SetUpAsync();
        await _upstreamClient.DeployAsync(UpstreamFeed, "com.example", "my-lib", "1.0");
        await _upstreamClient.DeployAsync(UpstreamFeed, "com.example", "my-lib", "2.0");
        await _downstreamClient.DeployAsync(MirrorFeed, "com.example", "my-lib", "1.5");

        var metadata = XDocument.Parse(await _downstreamClient.GetStringAsync(MavenTestClient.Url(MirrorFeed, "com/example/my-lib/maven-metadata.xml"))).Root!;

        Assert.Equal(new[] { "1.0", "1.5", "2.0" }, metadata.Element("versioning")?.Element("versions")?.Elements("version").Select(v => v.Value));
        Assert.Equal("2.0", metadata.Element("versioning")?.Element("release")?.Value);

        // Listing doesn't store anything.
        Assert.Equal(new[] { "1.5" }, (await GetStoredVersionsAsync()).Select(v => v.Version));
    }

    [Fact]
    public async Task StoresAFileOnItsFirstDownload()
    {
        await SetUpAsync();
        var jar = await _upstreamClient.DeployAsync(UpstreamFeed, "com.example", "my-lib", "1.0", description: "From upstream.");

        var pom = await _downstreamClient.GetStringAsync(MavenTestClient.FileUrl(MirrorFeed, "com.example", "my-lib", "1.0", "my-lib-1.0.pom"));
        var downloaded = await _downstreamClient.GetByteArrayAsync(MavenTestClient.FileUrl(MirrorFeed, "com.example", "my-lib", "1.0", "my-lib-1.0.jar"));
        var sha1 = await _downstreamClient.GetStringAsync(MavenTestClient.FileUrl(MirrorFeed, "com.example", "my-lib", "1.0", "my-lib-1.0.jar.sha1"));

        Assert.Contains("<artifactId>my-lib</artifactId>", pom);
        Assert.Equal(jar, downloaded);
        Assert.Equal(MavenTestClient.Checksums(jar)["sha1"], sha1);

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("1.0", stored.Version);
        Assert.Equal($"http://localhost/feeds/{UpstreamFeed}/maven2", stored.CachedFrom);
        Assert.Equal("From upstream.", stored.Description);
        Assert.Equal(1, stored.Downloads);
        Assert.Equal(new[] { "my-lib-1.0.jar", "my-lib-1.0.pom" }, stored.Files.Select(f => f.FileName).Order());

        // The mirrored package now shows up in the web UI of the mirror.
        Assert.Contains("com.example:my-lib", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/"));
    }

    [Fact]
    public async Task AVersionPublishedToTheFeedHidesTheMirror()
    {
        await SetUpAsync();
        await _upstreamClient.DeployAsync(UpstreamFeed, "com.example", "my-lib", "1.0");
        await _downstreamClient.PutFileAsync(
            MavenTestClient.FileUrl(MirrorFeed, "com.example", "my-lib", "1.0", "my-lib-1.0.pom"),
            Encoding.UTF8.GetBytes(MavenTestClient.Pom("com.example", "my-lib", "1.0", packaging: "pom")));

        using var jar = await _downstreamClient.GetAsync(MavenTestClient.FileUrl(MirrorFeed, "com.example", "my-lib", "1.0", "my-lib-1.0.jar"));

        Assert.Equal(HttpStatusCode.NotFound, jar.StatusCode);
    }

    [Fact]
    public async Task SnapshotsAreNotMirrored()
    {
        await SetUpAsync();
        await _upstreamClient.PutFileAsync(
            MavenTestClient.FileUrl(UpstreamFeed, "com.example", "my-lib", "1.0-SNAPSHOT", "my-lib-1.0-20200101.000000-1.jar"),
            MavenTestClient.Jar("snapshot"));

        using var metadata = await _downstreamClient.GetAsync(MavenTestClient.Url(MirrorFeed, "com/example/my-lib/1.0-SNAPSHOT/maven-metadata.xml"));
        using var jar = await _downstreamClient.GetAsync(MavenTestClient.FileUrl(MirrorFeed, "com.example", "my-lib", "1.0-SNAPSHOT", "my-lib-1.0-20200101.000000-1.jar"));

        Assert.Equal(HttpStatusCode.NotFound, metadata.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, jar.StatusCode);
    }

    [Fact]
    public async Task AFileThatDoesNotMatchTheMirrorsChecksumIsNotStored()
    {
        using var upstream = new FixedUpstreamHandler(new Dictionary<string, string>
        {
            ["/maven2/com/example/my-lib/1.0/my-lib-1.0.jar"] = "the jar",
            ["/maven2/com/example/my-lib/1.0/my-lib-1.0.jar.sha1"] = "0000000000000000000000000000000000000000",
        });
        using var app = new PaGettoApplication(_output, upstream);
        using var client = app.CreateClient();
        await app.CreateMavenFeedAsync(MirrorFeed, mirrorSource: "https://upstream.test/maven2");

        using var response = await client.GetAsync(MavenTestClient.FileUrl(MirrorFeed, "com.example", "my-lib", "1.0", "my-lib-1.0.jar"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await GetStoredVersionsAsync(app));
    }

    [Fact]
    public async Task ThePluginPrefixesOfAGroupComeFromTheMirror()
    {
        using var upstream = new FixedUpstreamHandler(new Dictionary<string, string>
        {
            ["/maven2/org/apache/maven/plugins/maven-metadata.xml"] = """
                <?xml version="1.0" encoding="UTF-8"?>
                <metadata>
                  <plugins>
                    <plugin><name>Apache Maven Dependency Plugin</name><prefix>dependency</prefix><artifactId>maven-dependency-plugin</artifactId></plugin>
                  </plugins>
                </metadata>
                """,
        });
        using var app = new PaGettoApplication(_output, upstream);
        using var client = app.CreateClient();
        await app.CreateMavenFeedAsync(MirrorFeed, mirrorSource: "https://upstream.test/maven2");

        var metadata = XDocument.Parse(await client.GetStringAsync(MavenTestClient.Url(MirrorFeed, "org/apache/maven/plugins/maven-metadata.xml"))).Root!;

        var plugin = Assert.Single(metadata.Element("plugins")!.Elements("plugin"));
        Assert.Equal("dependency", plugin.Element("prefix")?.Value);
        Assert.Equal("maven-dependency-plugin", plugin.Element("artifactId")?.Value);
        Assert.Null(metadata.Element("versioning"));
    }

    [Fact]
    public async Task UnknownArtifactIsNotFound()
    {
        await SetUpAsync();

        using var metadata = await _downstreamClient.GetAsync(MavenTestClient.Url(MirrorFeed, "com/example/unknown/maven-metadata.xml"));
        using var jar = await _downstreamClient.GetAsync(MavenTestClient.FileUrl(MirrorFeed, "com.example", "unknown", "1.0", "unknown-1.0.jar"));

        Assert.Equal(HttpStatusCode.NotFound, metadata.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, jar.StatusCode);
    }

    private async Task SetUpAsync()
    {
        await _upstream.CreateMavenFeedAsync(UpstreamFeed);
        await _downstream.CreateMavenFeedAsync(MirrorFeed, mirrorSource: $"http://localhost/feeds/{UpstreamFeed}/maven2");
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
