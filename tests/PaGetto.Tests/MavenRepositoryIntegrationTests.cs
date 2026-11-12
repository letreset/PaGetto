using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The Maven repository of a Maven feed, in Legacy mode without credentials (open access). The
/// requests are the ones Maven and Gradle send; see also the check with the real clients in the docs.
/// </summary>
public class MavenRepositoryIntegrationTests : IDisposable
{
    private const string Feed = "maven";

    private readonly ITestOutputHelper _output;
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public MavenRepositoryIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task DeployedFilesAreServedWithTheirChecksums()
    {
        await _app.CreateMavenFeedAsync(Feed);
        var jar = await _client.DeployAsync(Feed, "com.example", "my-lib", "1.0");
        var sources = MavenTestClient.Jar("sources");
        await _client.PutFileAsync(MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "1.0", "my-lib-1.0-sources.jar"), sources);

        var url = MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "1.0", "my-lib-1.0.jar");
        using var download = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(jar, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/java-archive", download.Content.Headers.ContentType?.MediaType);

        var checksums = MavenTestClient.Checksums(jar);
        Assert.Equal(checksums["sha1"], download.Headers.GetValues("X-Checksum-SHA1").Single());
        Assert.Equal(checksums["md5"], download.Headers.GetValues("X-Checksum-MD5").Single());
        foreach (var (extension, value) in checksums)
        {
            Assert.Equal(value, await _client.GetStringAsync($"{url}.{extension}"));
        }

        var sourcesUrl = MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "1.0", "my-lib-1.0-sources.jar");
        Assert.Equal(sources, await _client.GetByteArrayAsync(sourcesUrl));

        using var head = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(jar.Length, head.Content.Headers.ContentLength);

        using var missing = await _client.GetAsync(MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "1.0", "my-lib-1.0-javadoc.jar"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task TheArtifactMetadataListsTheVersions()
    {
        await _app.CreateMavenFeedAsync(Feed);
        await _client.DeployAsync(Feed, "com.example", "my-lib", "1.10");
        await _client.DeployAsync(Feed, "com.example", "my-lib", "1.9");
        await DeploySnapshotBuildAsync("2.0-SNAPSHOT", "20200101.000000", 1);

        var url = MavenTestClient.Url(Feed, "com/example/my-lib/maven-metadata.xml");
        var bytes = await _client.GetByteArrayAsync(url);
        var metadata = XDocument.Parse(Encoding.UTF8.GetString(bytes)).Root!;

        Assert.Equal("com.example", metadata.Element("groupId")?.Value);
        Assert.Equal("my-lib", metadata.Element("artifactId")?.Value);
        Assert.Equal("2.0-SNAPSHOT", metadata.Element("versioning")?.Element("latest")?.Value);
        Assert.Equal("1.10", metadata.Element("versioning")?.Element("release")?.Value);
        Assert.Equal(new[] { "1.9", "1.10", "2.0-SNAPSHOT" }, metadata.Element("versioning")?.Element("versions")?.Elements("version").Select(v => v.Value));

#pragma warning disable CA5350 // The checksum Maven checks the metadata with.
        Assert.Equal(Convert.ToHexStringLower(SHA1.HashData(bytes)), await _client.GetStringAsync(url + ".sha1"));
#pragma warning restore CA5350

        using var unknown = await _client.GetAsync(MavenTestClient.Url(Feed, "com/example/other/maven-metadata.xml"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task UnlistedVersionsAreLeftOutOfTheMetadataButStillDownload()
    {
        var feed = await _app.CreateMavenFeedAsync(Feed);
        await _client.DeployAsync(Feed, "com.example", "my-lib", "1.0");
        await _client.DeployAsync(Feed, "com.example", "my-lib", "2.0");

        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IArtifactDatabase>()
                .SetListedAsync(feed.Id, "com.example:my-lib", "2.0", listed: false, CancellationToken.None);
        }

        var metadata = await _client.GetStringAsync(MavenTestClient.Url(Feed, "com/example/my-lib/maven-metadata.xml"));
        using var download = await _client.GetAsync(MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "2.0", "my-lib-2.0.jar"));

        Assert.Contains("<version>1.0</version>", metadata);
        Assert.DoesNotContain("<version>2.0</version>", metadata);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Fact]
    public async Task SnapshotBuildsAreListedInTheSnapshotMetadata()
    {
        await _app.CreateMavenFeedAsync(Feed);
        var first = await DeploySnapshotBuildAsync("1.0-SNAPSHOT", "20200101.000000", 1);
        var second = await DeploySnapshotBuildAsync("1.0-SNAPSHOT", "20200102.000000", 2);

        var metadata = XDocument.Parse(await _client.GetStringAsync(MavenTestClient.Url(Feed, "com/example/my-lib/1.0-SNAPSHOT/maven-metadata.xml"))).Root!;
        var versioning = metadata.Element("versioning")!;

        Assert.Equal("1.0-SNAPSHOT", metadata.Element("version")?.Value);
        Assert.Equal("20200102.000000", versioning.Element("snapshot")?.Element("timestamp")?.Value);
        Assert.Equal("2", versioning.Element("snapshot")?.Element("buildNumber")?.Value);
        Assert.All(
            versioning.Element("snapshotVersions")!.Elements("snapshotVersion"),
            v => Assert.Equal("1.0-20200102.000000-2", v.Element("value")?.Value));

        // Earlier builds stay downloadable.
        Assert.Equal(first, await _client.GetByteArrayAsync(MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "1.0-SNAPSHOT", "my-lib-1.0-20200101.000000-1.jar")));
        Assert.Equal(second, await _client.GetByteArrayAsync(MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "1.0-SNAPSHOT", "my-lib-1.0-20200102.000000-2.jar")));
    }

    [Fact]
    public async Task ReleaseFilesAreNotOverwrittenByDefault()
    {
        await _app.CreateMavenFeedAsync(Feed);
        var jar = await _client.DeployAsync(Feed, "com.example", "my-lib", "1.0");
        var url = MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "1.0", "my-lib-1.0.jar");

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
    [InlineData(PackageOverwriteAllowed.True, "1.0", true)]
    [InlineData(PackageOverwriteAllowed.PrereleaseOnly, "1.0-rc1", true)]
    [InlineData(PackageOverwriteAllowed.PrereleaseOnly, "1.0", false)]
    [InlineData(PackageOverwriteAllowed.False, "1.0-SNAPSHOT", true)]
    public async Task TheOverwritePolicyAppliesToReleasesAndSnapshotsAreAlwaysReplaced(PackageOverwriteAllowed policy, string version, bool replaced)
    {
        await _app.CreateMavenFeedAsync(Feed, configure: f => f.AllowPackageOverwrites = policy);
        var url = MavenTestClient.FileUrl(Feed, "com.example", "my-lib", version, $"my-lib-{version}.jar");
        await _client.PutFileAsync(url, MavenTestClient.Jar("first"));

        using var overwrite = await _client.PutBytesAsync(url, MavenTestClient.Jar("second"));

        Assert.Equal(replaced ? HttpStatusCode.Created : HttpStatusCode.Conflict, overwrite.StatusCode);
        Assert.Equal(MavenTestClient.Jar(replaced ? "second" : "first"), await _client.GetByteArrayAsync(url));
        Assert.Equal(MavenTestClient.Checksums(MavenTestClient.Jar(replaced ? "second" : "first"))["sha1"], await _client.GetStringAsync(url + ".sha1"));
    }

    [Fact]
    public async Task InvalidUploadsAreRejected()
    {
        await _app.CreateMavenFeedAsync(Feed);
        await _client.DeployAsync(Feed, "com.example", "my-lib", "1.0");

        using var otherPom = await _client.PutBytesAsync(
            MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "2.0", "my-lib-2.0.pom"),
            Encoding.UTF8.GetBytes(MavenTestClient.Pom("com.example", "other", "2.0")));
        using var brokenPom = await _client.PutBytesAsync(
            MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "2.0", "my-lib-2.0.pom"),
            Encoding.UTF8.GetBytes("not xml"));
        using var notTheLayout = await _client.PutBytesAsync(MavenTestClient.Url(Feed, "com/example/my-lib/2.0/other-2.0.jar"), [1]);
        using var wrongChecksum = await _client.PutAsync(
            MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "1.0", "my-lib-1.0.jar.sha1"),
            new StringContent("0000000000000000000000000000000000000000"));

        Assert.Equal(HttpStatusCode.BadRequest, otherPom.StatusCode);
        Assert.Contains("com.example:other:2.0", otherPom.ReasonPhrase);
        Assert.Equal(HttpStatusCode.BadRequest, brokenPom.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notTheLayout.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongChecksum.StatusCode);
        Assert.Contains("does not match", await wrongChecksum.Content.ReadAsStringAsync());

        // Nothing of the rejected uploads was stored.
        using var version = await _client.GetAsync(MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "2.0", "my-lib-2.0.pom"));
        Assert.Equal(HttpStatusCode.NotFound, version.StatusCode);
    }

    [Fact]
    public async Task APomWithAPropertyAsItsVersionIsAccepted()
    {
        await _app.CreateMavenFeedAsync(Feed);

        // A CI-friendly version deployed without the flatten plugin.
        await _client.PutFileAsync(
            MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "1.0", "my-lib-1.0.pom"),
            Encoding.UTF8.GetBytes(MavenTestClient.Pom("com.example", "my-lib", "${revision}")));
    }

    [Fact]
    public async Task ReadOnlyFeedRejectsUploads()
    {
        await _app.CreateMavenFeedAsync(Feed, configure: f => f.IsReadOnlyMode = true);

        using var response = await _client.PutBytesAsync(MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "1.0", "my-lib-1.0.jar"), [1]);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("This feed is read-only.", response.ReasonPhrase);
    }

    [Fact]
    public async Task FileLargerThanTheFeedAllowsIsRejected()
    {
        await _app.CreateMavenFeedAsync(Feed, configure: f => f.MaxPackageSizeMiB = 1);

        using var response = await _client.PutBytesAsync(
            MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "1.0", "my-lib-1.0.jar"),
            new byte[(1024 * 1024) + 1]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("1 MiB", response.ReasonPhrase);
    }

    [Fact]
    public async Task DownloadsOfTheMainFileAreCounted()
    {
        var feed = await _app.CreateMavenFeedAsync(Feed);
        await _client.DeployAsync(Feed, "com.example", "my-lib", "1.0");
        var jarUrl = MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "1.0", "my-lib-1.0.jar");

        await _client.GetByteArrayAsync(jarUrl);
        await _client.GetByteArrayAsync(jarUrl);
        await _client.GetStringAsync(jarUrl + ".sha1");
        await _client.GetByteArrayAsync(MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "1.0", "my-lib-1.0.pom"));
        using (await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, jarUrl)))
        {
        }

        using var scope = _app.Services.CreateScope();
        var version = await scope.ServiceProvider.GetRequiredService<IArtifactDatabase>()
            .FindVersionOrNullAsync(feed.Id, "com.example:my-lib", "1.0", CancellationToken.None);

        Assert.Equal(2, version.Downloads);
        Assert.Equal("A test library.", version.Description);
    }

    [Fact]
    public async Task TheRepositoryOnlyAnswersOnMavenFeeds()
    {
        await _app.CreateMavenFeedAsync(Feed);
        await _app.CreateFeedAsync("nuget");

        using var mavenOnNuGet = await _client.GetAsync("feeds/nuget/maven2/com/example/my-lib/maven-metadata.xml");
        using var mavenOnDefault = await _client.GetAsync("maven2/com/example/my-lib/maven-metadata.xml");
        using var nugetOnMaven = await _client.GetAsync($"feeds/{Feed}/v3/index.json");

        Assert.Equal(HttpStatusCode.NotFound, mavenOnNuGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, mavenOnDefault.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nugetOnMaven.StatusCode);
    }

    [Fact]
    public async Task ApiKeysOfLegacyModeAreSentAsThePassword()
    {
        using var app = new PaGettoApplication(_output, null, dict => dict["Authentication:ApiKeys:0:Key"] = "secret");
        using var client = app.CreateClient();
        await app.CreateMavenFeedAsync(Feed);
        var url = MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "1.0", "my-lib-1.0.jar");

        // Maven only sends credentials when challenged.
        using (var anonymous = await client.PutBytesAsync(url, [1]))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.Equal("Basic realm=\"PaGetto\"", anonymous.Headers.WwwAuthenticate.ToString());
        }

        client.DefaultRequestHeaders.Authorization = Basic("deployer", "wrong");
        using (var wrongKey = await client.PutBytesAsync(url, [1]))
        {
            Assert.Equal(HttpStatusCode.Forbidden, wrongKey.StatusCode);
        }

        client.DefaultRequestHeaders.Authorization = Basic("deployer", "secret");
        using var deployed = await client.PutBytesAsync(url, [1]);

        Assert.Equal(HttpStatusCode.Created, deployed.StatusCode);
    }

    [Fact]
    public async Task ThePackageIsShownInTheWebUi()
    {
        await _app.CreateMavenFeedAsync(Feed);
        await _client.DeployAsync(Feed, "com.example", "My-Lib", "1.0", description: "Mixed case.");

        var list = await _client.GetStringAsync($"feeds/{Feed}/");
        var page = await _client.GetStringAsync($"feeds/{Feed}/artifacts/com.example:my-lib");
        var connect = await _client.GetStringAsync($"feeds/{Feed}/connect");

        // The coordinates as published, though the lookup ignores case.
        Assert.Contains("com.example:My-Lib", list);
        Assert.Contains("Mixed case.", page);
        Assert.Contains("My-Lib-1.0.jar", page);
        Assert.Contains("implementation(\\u0022com.example:My-Lib:1.0\\u0022)", page);
        Assert.Contains("org.slf4j:slf4j-api", page);
        Assert.Contains("http://localhost/feeds/maven/maven2/", connect);
    }

    /// <summary>
    /// Deploys one build of a snapshot as Maven does: timestamped files.
    /// </summary>
    /// <returns>The JAR of the build.</returns>
    private async Task<byte[]> DeploySnapshotBuildAsync(string version, string timestamp, int buildNumber)
    {
        var fileVersion = $"{version[..^"SNAPSHOT".Length]}{timestamp}-{buildNumber}";
        var jar = MavenTestClient.Jar(fileVersion);

        await _client.PutFileAsync(MavenTestClient.FileUrl(Feed, "com.example", "my-lib", version, $"my-lib-{fileVersion}.jar"), jar);
        await _client.PutFileAsync(
            MavenTestClient.FileUrl(Feed, "com.example", "my-lib", version, $"my-lib-{fileVersion}.pom"),
            Encoding.UTF8.GetBytes(MavenTestClient.Pom("com.example", "my-lib", version)));
        await _client.PutFileAsync(MavenTestClient.Url(Feed, $"com/example/my-lib/{version}/maven-metadata.xml"), Encoding.UTF8.GetBytes("<metadata/>"));

        return jar;
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
