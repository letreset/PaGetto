using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Ivy;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The Ivy repository of an Ivy feed, in Legacy mode without credentials (open access). The requests
/// are the ones sbt sends with <c>Resolver.ivyStylePatterns</c>; see also the check with the real
/// client in the docs.
/// </summary>
public class IvyRepositoryIntegrationTests : IDisposable
{
    private const string Feed = "sbt";
    private const string PackageName = "com.example:sbt-tool:scala_2.12:sbt_1.0";

    private readonly ITestOutputHelper _output;
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public IvyRepositoryIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task PublishedFilesAreServedWithTheirChecksums()
    {
        await _app.CreateIvyFeedAsync(Feed);
        var jar = await _client.PublishPluginAsync(Feed, "sbt-tool", "1.0.0");

        var url = IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "1.0.0", "jars/sbt-tool.jar");
        using var download = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(jar, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/java-archive", download.Content.Headers.ContentType?.MediaType);

        var checksums = MavenTestClient.Checksums(jar);
        Assert.Equal(checksums["sha1"], download.Headers.GetValues("X-Checksum-SHA1").Single());
        foreach (var (extension, value) in checksums)
        {
            Assert.Equal(value, await _client.GetStringAsync($"{url}.{extension}"));
        }

        var descriptor = await _client.GetStringAsync(IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "1.0.0", IvyPath.DescriptorFileName));
        Assert.Contains("module=\"sbt-tool\"", descriptor);

        using var head = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(jar.Length, head.Content.Headers.ContentLength);

        using var missing = await _client.GetAsync(IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "1.0.0", "srcs/sbt-tool-sources.jar"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // The cross versions are part of the module: the same revision for sbt 0.13 is another one.
        using var otherSbt = await _client.GetAsync(IvyTestClient.Url(Feed, "com.example/sbt-tool/scala_2.10/sbt_0.13/1.0.0/jars/sbt-tool.jar"));
        Assert.Equal(HttpStatusCode.NotFound, otherSbt.StatusCode);
    }

    [Fact]
    public async Task ALibraryWithoutCrossVersionFoldersIsPublished()
    {
        await _app.CreateIvyFeedAsync(Feed);
        var url = IvyTestClient.Url(Feed, "com.example/my-lib_2.13/1.0.0/jars/my-lib_2.13.jar");

        await _client.PutFileAsync(url, MavenTestClient.Jar("library"));

        Assert.Equal(MavenTestClient.Jar("library"), await _client.GetByteArrayAsync(url));
    }

    [Fact]
    public async Task TheFolderOfAModuleListsItsListedRevisions()
    {
        var feed = await _app.CreateIvyFeedAsync(Feed);
        await _client.PublishPluginAsync(Feed, "sbt-tool", "1.10.0");
        await _client.PublishPluginAsync(Feed, "sbt-tool", "1.9.0");
        await _client.PublishPluginAsync(Feed, "sbt-tool", "2.0.0");

        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IArtifactDatabase>()
                .SetListedAsync(feed.Id, PackageName, "2.0.0", listed: false, CancellationToken.None);
        }

        using var listing = await _client.GetAsync(IvyTestClient.Url(Feed, "com.example/sbt-tool/scala_2.12/sbt_1.0/"));
        var html = await listing.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        Assert.Equal("text/html", listing.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            new[] { "1.9.0", "1.10.0" },
            IvyUpstreamClient.ParseFolders(new Uri("http://localhost/feeds/sbt/ivy/com.example/sbt-tool/scala_2.12/sbt_1.0/"), html));

        // An unlisted revision still downloads.
        using var unlisted = await _client.GetAsync(IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "2.0.0", "jars/sbt-tool.jar"));
        Assert.Equal(HttpStatusCode.OK, unlisted.StatusCode);

        using var unknown = await _client.GetAsync(IvyTestClient.Url(Feed, "com.example/other/scala_2.12/sbt_1.0/"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task TheDescriptorIsTheManifestOfTheRevision()
    {
        var feed = await _app.CreateIvyFeedAsync(Feed);
        await _client.PublishPluginAsync(Feed, "sbt-tool", "1.0.0", description: "From the descriptor.");

        using var scope = _app.Services.CreateScope();
        var version = await scope.ServiceProvider.GetRequiredService<IArtifactDatabase>()
            .FindVersionOrNullAsync(feed.Id, PackageName, "1.0.0", CancellationToken.None);
        var files = await scope.ServiceProvider.GetRequiredService<IArtifactDatabase>()
            .FindFilesAsync(feed.Id, PackageName, "1.0.0", CancellationToken.None);

        Assert.Equal("From the descriptor.", version.Description);
        Assert.Equal("release", IvyManifestSummary.Parse(version.MetadataJson).Status);
        Assert.Equal(new[] { "ivys/ivy.xml", "jars/sbt-tool.jar" }, files.Select(f => f.FileName).Order());
    }

    [Fact]
    public async Task ReleaseFilesAreNotOverwrittenByDefault()
    {
        await _app.CreateIvyFeedAsync(Feed);
        var jar = await _client.PublishPluginAsync(Feed, "sbt-tool", "1.0.0");
        var url = IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "1.0.0", "jars/sbt-tool.jar");

        // The same content again is a retry, which succeeds.
        using (var retry = await _client.PutBytesAsync(url, jar))
        {
            Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        }

        using var overwrite = await _client.PutBytesAsync(url, MavenTestClient.Jar("other"));

        Assert.Equal(HttpStatusCode.Conflict, overwrite.StatusCode);
        Assert.Contains("does not allow overwriting", overwrite.ReasonPhrase);
        Assert.Equal(jar, await _client.GetByteArrayAsync(url));
    }

    [Theory]
    [InlineData(PackageOverwriteAllowed.True, "1.0.0", true)]
    [InlineData(PackageOverwriteAllowed.PrereleaseOnly, "1.0.0-RC1", true)]
    [InlineData(PackageOverwriteAllowed.PrereleaseOnly, "1.0.0", false)]
    [InlineData(PackageOverwriteAllowed.False, "1.0.0-SNAPSHOT", true)]
    public async Task TheOverwritePolicyAppliesToReleasesAndSnapshotsAreAlwaysReplaced(PackageOverwriteAllowed policy, string revision, bool replaced)
    {
        await _app.CreateIvyFeedAsync(Feed, configure: f => f.AllowPackageOverwrites = policy);
        var url = IvyTestClient.PluginFileUrl(Feed, "sbt-tool", revision, "jars/sbt-tool.jar");
        await _client.PutFileAsync(url, MavenTestClient.Jar("first"));

        using var overwrite = await _client.PutBytesAsync(url, MavenTestClient.Jar("second"));

        Assert.Equal(replaced ? HttpStatusCode.Created : HttpStatusCode.Conflict, overwrite.StatusCode);
        Assert.Equal(MavenTestClient.Jar(replaced ? "second" : "first"), await _client.GetByteArrayAsync(url));
    }

    [Fact]
    public async Task InvalidUploadsAreRejected()
    {
        await _app.CreateIvyFeedAsync(Feed);
        await _client.PublishPluginAsync(Feed, "sbt-tool", "1.0.0");

        using var otherDescriptor = await _client.PutBytesAsync(
            IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "2.0.0", IvyPath.DescriptorFileName),
            Encoding.UTF8.GetBytes(IvyTestClient.PluginDescriptor("sbt-other", "2.0.0")));
        using var brokenDescriptor = await _client.PutBytesAsync(
            IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "2.0.0", IvyPath.DescriptorFileName),
            Encoding.UTF8.GetBytes("not xml"));
        using var notTheLayout = await _client.PutBytesAsync(IvyTestClient.Url(Feed, "com.example/sbt-tool/2.0.0/sbt-tool.jar"), [1]);
        using var folder = await _client.PutBytesAsync(IvyTestClient.Url(Feed, "com.example/sbt-tool/"), [1]);
        using var wrongChecksum = await _client.PutAsync(
            IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "1.0.0", "jars/sbt-tool.jar.sha1"),
            new StringContent("0000000000000000000000000000000000000000"));

        Assert.Equal(HttpStatusCode.BadRequest, otherDescriptor.StatusCode);
        Assert.Contains("com.example:sbt-other:scala_2.12:sbt_1.0 2.0.0", otherDescriptor.ReasonPhrase);
        Assert.Equal(HttpStatusCode.BadRequest, brokenDescriptor.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notTheLayout.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, folder.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongChecksum.StatusCode);
        Assert.Contains("does not match", await wrongChecksum.Content.ReadAsStringAsync());

        // Nothing of the rejected uploads was stored.
        using var revision = await _client.GetAsync(IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "2.0.0", IvyPath.DescriptorFileName));
        Assert.Equal(HttpStatusCode.NotFound, revision.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyFeedRejectsUploads()
    {
        await _app.CreateIvyFeedAsync(Feed, configure: f => f.IsReadOnlyMode = true);

        using var response = await _client.PutBytesAsync(IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "1.0.0", "jars/sbt-tool.jar"), [1]);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("This feed is read-only.", response.ReasonPhrase);
    }

    [Fact]
    public async Task FileLargerThanTheFeedAllowsIsRejected()
    {
        await _app.CreateIvyFeedAsync(Feed, configure: f => f.MaxPackageSizeMiB = 1);

        using var response = await _client.PutBytesAsync(
            IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "1.0.0", "jars/sbt-tool.jar"),
            new byte[(1024 * 1024) + 1]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("1 MiB", response.ReasonPhrase);
    }

    [Fact]
    public async Task DownloadsOfTheModulesArtifactAreCounted()
    {
        var feed = await _app.CreateIvyFeedAsync(Feed);
        await _client.PublishPluginAsync(Feed, "sbt-tool", "1.0.0");
        await _client.PutFileAsync(IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "1.0.0", "srcs/sbt-tool-sources.jar"), MavenTestClient.Jar("sources"));
        var jarUrl = IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "1.0.0", "jars/sbt-tool.jar");

        await _client.GetByteArrayAsync(jarUrl);
        await _client.GetByteArrayAsync(jarUrl);
        await _client.GetStringAsync(jarUrl + ".sha1");
        await _client.GetByteArrayAsync(IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "1.0.0", IvyPath.DescriptorFileName));
        await _client.GetByteArrayAsync(IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "1.0.0", "srcs/sbt-tool-sources.jar"));
        using (await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, jarUrl)))
        {
        }

        using var scope = _app.Services.CreateScope();
        var version = await scope.ServiceProvider.GetRequiredService<IArtifactDatabase>()
            .FindVersionOrNullAsync(feed.Id, PackageName, "1.0.0", CancellationToken.None);

        Assert.Equal(2, version.Downloads);
    }

    [Fact]
    public async Task TheRepositoryOnlyAnswersOnIvyFeeds()
    {
        await _app.CreateIvyFeedAsync(Feed);
        await _app.CreateMavenFeedAsync("maven");

        using var ivyOnMaven = await _client.GetAsync("feeds/maven/ivy/com.example/sbt-tool/");
        using var ivyOnDefault = await _client.GetAsync("ivy/com.example/sbt-tool/");
        using var mavenOnIvy = await _client.GetAsync($"feeds/{Feed}/maven2/com/example/my-lib/maven-metadata.xml");

        Assert.Equal(HttpStatusCode.NotFound, ivyOnMaven.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ivyOnDefault.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, mavenOnIvy.StatusCode);
    }

    [Fact]
    public async Task ApiKeysOfLegacyModeAreSentAsThePassword()
    {
        using var app = new PaGettoApplication(_output, null, dict => dict["Authentication:ApiKeys:0:Key"] = "secret");
        using var client = app.CreateClient();
        await app.CreateIvyFeedAsync(Feed);
        var url = IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "1.0.0", "jars/sbt-tool.jar");

        // sbt only sends credentials when challenged.
        using (var anonymous = await client.PutBytesAsync(url, [1]))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.Equal("Basic realm=\"PaGetto\"", anonymous.Headers.WwwAuthenticate.ToString());
        }

        client.DefaultRequestHeaders.Authorization = Basic("publisher", "wrong");
        using (var wrongKey = await client.PutBytesAsync(url, [1]))
        {
            Assert.Equal(HttpStatusCode.Forbidden, wrongKey.StatusCode);
        }

        client.DefaultRequestHeaders.Authorization = Basic("publisher", "secret");
        using var published = await client.PutBytesAsync(url, [1]);

        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
    }

    [Fact]
    public async Task ThePackageIsShownInTheWebUi()
    {
        await _app.CreateIvyFeedAsync(Feed);
        await _client.PublishPluginAsync(Feed, "sbt-Tool", "1.0.0", description: "Mixed case.");

        var list = await _client.GetStringAsync($"feeds/{Feed}/");
        var page = await _client.GetStringAsync($"feeds/{Feed}/artifacts/com.example:sbt-tool:scala_2.12:sbt_1.0");
        var connect = await _client.GetStringAsync($"feeds/{Feed}/connect");

        // The module as published, though the lookup ignores case.
        Assert.Contains("com.example:sbt-Tool:scala_2.12:sbt_1.0", list);
        Assert.Contains("data-copy=\"addSbtPlugin(&quot;com.example&quot; % &quot;sbt-Tool&quot; % &quot;1.0.0&quot;)\"", list);
        Assert.Contains("Mixed case.", page);
        Assert.Contains("http://localhost/feeds/sbt/ivy/com.example/sbt-Tool/scala_2.12/sbt_1.0/1.0.0/jars/sbt-Tool.jar", page);
        Assert.Contains("addSbtPlugin(\\u0022com.example\\u0022 % \\u0022sbt-Tool\\u0022 % \\u00221.0.0\\u0022)", page);
        Assert.Contains("Resolver.ivyStylePatterns", page);
        Assert.Contains("org.scala-lang:scala-library", page);
        Assert.Contains("https://example.com/sbt-Tool", page);
        Assert.Contains("http://localhost/feeds/sbt/ivy", connect);
        Assert.Contains("realm=PaGetto", connect);
    }

    private static AuthenticationHeaderValue Basic(string username, string password)
    {
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
