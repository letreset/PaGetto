using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.CocoaPods;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The spec repository of a CocoaPods feed, in Legacy mode without credentials (open access).
/// </summary>
public class CocoaPodsRepositoryIntegrationTests : IDisposable
{
    private const string Feed = "pods";
    private const string Pod = "MyLib";

    private readonly ITestOutputHelper _output;
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public CocoaPodsRepositoryIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task RepositoryFilesTellCocoaPodsItIsACdnRepository()
    {
        await _app.CreateCocoaPodsFeedAsync(Feed);

        var version = await _client.GetStringAsync($"feeds/{Feed}/cocoapods/CocoaPods-version.yml");
        using var deprecated = await _client.GetAsync($"feeds/{Feed}/cocoapods/deprecated_podspecs.txt");
        using var allPods = await _client.GetAsync($"feeds/{Feed}/cocoapods/all_pods.txt");
        using var index = await _client.GetAsync(CocoaPodsTestClient.IndexUrl(Feed, Pod));

        // CocoaPods takes a URL for a CDN repository when this file has a "last" version.
        Assert.Contains("last: 1.8.0", version);
        Assert.Contains("prefix_lengths:\n- 1\n- 1\n- 1\n", version);
        Assert.Equal(HttpStatusCode.OK, deprecated.StatusCode);
        Assert.Empty(await deprecated.Content.ReadAsStringAsync());
        Assert.Empty(await allPods.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Empty(await index.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PublishedPodspecIsServedToCocoaPods()
    {
        await _app.CreateCocoaPodsFeedAsync(Feed);
        var podspec = CocoaPodsTestClient.Podspec(Pod, "1.0.0");

        using var publish = await _client.PublishAsync(Feed, podspec);

        Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        Assert.Equal("Published MyLib 1.0.0.", await publish.Content.ReadAsStringAsync());

        Assert.Equal(new[] { "1.0.0" }, await _client.GetPodVersionsAsync(Feed, Pod));
        Assert.Equal("MyLib\n", await _client.GetStringAsync($"feeds/{Feed}/cocoapods/all_pods.txt"));

        using var response = await _client.GetAsync(CocoaPodsTestClient.PodspecUrl(Feed, Pod, "1.0.0"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(podspec, await response.Content.ReadAsStringAsync());

        var stored = await GetVersionAsync("1.0.0");
        Assert.Equal("!my!lib", stored.Artifact.Name);
        Assert.Equal("A test pod", stored.Description);
        Assert.Null(stored.ContentHash);
        Assert.False(stored.IsPrerelease);
        Assert.Null(stored.CachedFrom);
    }

    [Fact]
    public async Task SourceArchiveIsStoredAndThePodspecPointsAtIt()
    {
        await _app.CreateCocoaPodsFeedAsync(Feed);
        var zip = CocoaPodsTestClient.SourceZip();
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(zip));

        using var publish = await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0"), zip, "MyLib-1.0.0.zip");

        Assert.Equal(HttpStatusCode.Created, publish.StatusCode);

        var podspec = await _client.GetPodspecAsync(Feed, Pod, "1.0.0");
        var url = $"http://localhost/feeds/{Feed}/cocoapods/sources/MyLib/1.0.0/MyLib-1.0.0.zip";
        Assert.Equal(
            $$"""{"http":"{{url}}","type":"zip","sha256":"{{sha256}}"}""",
            podspec["source"]!.ToJsonString());

        using var download = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(zip, await download.Content.ReadAsByteArrayAsync());

        var stored = await GetVersionAsync("1.0.0");
        Assert.Equal(sha256, stored.ContentHash);
        Assert.Equal(zip.Length, stored.Size);
        Assert.Equal(1, stored.Downloads);
    }

    [Fact]
    public async Task PodNamesAreCaseSensitive()
    {
        await _app.CreateCocoaPodsFeedAsync(Feed);
        using (await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec("MyLib", "1.0.0")))
        {
        }

        using var other = await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec("mylib", "2.0.0"));
        using var wrongCase = await _client.GetAsync(CocoaPodsTestClient.PodspecUrl(Feed, "MYLIB", "1.0.0"));

        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        Assert.Equal(new[] { "1.0.0" }, await _client.GetPodVersionsAsync(Feed, "MyLib"));
        Assert.Equal(new[] { "2.0.0" }, await _client.GetPodVersionsAsync(Feed, "mylib"));
        Assert.Equal(HttpStatusCode.NotFound, wrongCase.StatusCode);
        Assert.Equal("MyLib\nmylib\n", await _client.GetStringAsync($"feeds/{Feed}/cocoapods/all_pods.txt"));
    }

    [Fact]
    public async Task VersionsAreListedInOrderWithTheirPrereleases()
    {
        await _app.CreateCocoaPodsFeedAsync(Feed);
        foreach (var version in new[] { "1.10.0", "1.0.0", "2.0.0-beta.1", "1.9.0" })
        {
            using var publish = await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, version));
            Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        }

        Assert.Equal(new[] { "1.0.0", "1.9.0", "1.10.0", "2.0.0-beta.1" }, await _client.GetPodVersionsAsync(Feed, Pod));
        Assert.True((await GetVersionAsync("2.0.0-beta.1")).IsPrerelease);
    }

    [Fact]
    public async Task UnlistedVersionIsLeftOutOfTheIndexButStillServed()
    {
        await _app.CreateCocoaPodsFeedAsync(Feed);
        using (await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0")))
        {
        }

        using (await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.1.0")))
        {
        }

        await SetListedAsync("1.1.0", listed: false);

        using var podspec = await _client.GetAsync(CocoaPodsTestClient.PodspecUrl(Feed, Pod, "1.1.0"));

        Assert.Equal(new[] { "1.0.0" }, await _client.GetPodVersionsAsync(Feed, Pod));
        Assert.Equal(HttpStatusCode.OK, podspec.StatusCode);
    }

    [Fact]
    public async Task ExistingVersionIsRefused()
    {
        await _app.CreateCocoaPodsFeedAsync(Feed);
        using (await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0")))
        {
        }

        using var again = await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0", p => p["summary"] = "changed"));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("Version 1.0.0 of pod MyLib already exists.", await again.Content.ReadAsStringAsync());
        Assert.Equal("A test pod", (await _client.GetPodspecAsync(Feed, Pod, "1.0.0"))["summary"]!.GetValue<string>());
    }

    [Fact]
    public async Task OverwritingFeedReplacesTheVersionAndItsArchive()
    {
        await _app.CreateCocoaPodsFeedAsync(Feed, configure: f => f.AllowPackageOverwrites = PackageOverwriteAllowed.True);
        using (await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0"), CocoaPodsTestClient.SourceZip("first"), "a.zip"))
        {
        }

        var replacement = CocoaPodsTestClient.SourceZip("second");
        using var response = await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0"), replacement, "b.tar.gz");

        var source = (await _client.GetPodspecAsync(Feed, Pod, "1.0.0"))["source"]!;
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("tgz", source["type"]!.GetValue<string>());
        Assert.Equal(replacement, await _client.GetByteArrayAsync(source["http"]!.GetValue<string>()));
    }

    [Fact]
    public async Task PrereleaseOnlyFeedReplacesOnlyPrereleases()
    {
        await _app.CreateCocoaPodsFeedAsync(Feed, configure: f => f.AllowPackageOverwrites = PackageOverwriteAllowed.PrereleaseOnly);
        foreach (var version in new[] { "1.0.0", "2.0.0-beta.1" })
        {
            using (await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, version)))
            {
            }
        }

        using var release = await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0"));
        using var prerelease = await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "2.0.0-beta.1"));

        Assert.Equal(HttpStatusCode.Conflict, release.StatusCode);
        Assert.Equal(HttpStatusCode.Created, prerelease.StatusCode);
    }

    public static IEnumerable<object[]> InvalidPublishes()
    {
        yield return ["Pod::Spec.new do |s|", null, null, "not valid JSON"];
        yield return [CocoaPodsTestClient.Podspec("My Lib", "1.0.0"), null, null, "My Lib is not a pod name"];
        yield return [CocoaPodsTestClient.Podspec(Pod, "v1.0"), null, null, "v1.0 is not a version CocoaPods accepts"];
        yield return [CocoaPodsTestClient.Podspec(Pod, "1.0.0", p => p.Remove("source")), null, null, "The podspec has no source"];
        yield return [CocoaPodsTestClient.Podspec(Pod, "1.0.0"), CocoaPodsTestClient.SourceZip(), "MyLib.dmg", "MyLib.dmg is not a source archive"];
    }

    [Theory]
    [MemberData(nameof(InvalidPublishes))]
    public async Task InvalidPublishIsRejectedWithAMessage(string podspec, byte[] source, string sourceFileName, string message)
    {
        await _app.CreateCocoaPodsFeedAsync(Feed);

        using var response = await _client.PublishAsync(Feed, podspec, source, sourceFileName);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PodspecWithoutSourceIsAcceptedWithItsArchive()
    {
        await _app.CreateCocoaPodsFeedAsync(Feed);

        using var response = await _client.PublishAsync(
            Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0", p => p.Remove("source")), CocoaPodsTestClient.SourceZip(), "MyLib.zip");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PublishThatIsNotAFormIsRejected()
    {
        await _app.CreateCocoaPodsFeedAsync(Feed);

        using var json = await _client.PostAsync($"feeds/{Feed}/cocoapods/api/v1/pods", new StringContent(CocoaPodsTestClient.Podspec(Pod, "1.0.0")));
        using var empty = await _client.PostAsync($"feeds/{Feed}/cocoapods/api/v1/pods", new MultipartFormDataContent { { new StringContent("x"), "other" } });

        Assert.Equal(HttpStatusCode.BadRequest, json.StatusCode);
        Assert.Contains("multipart form", await json.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("no podspec", await empty.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadOnlyFeedRefusesPublishing()
    {
        await _app.CreateCocoaPodsFeedAsync(Feed, configure: f => f.IsReadOnlyMode = true);

        using var response = await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("read-only", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SourceArchiveLargerThanTheFeedLimitIsRejected()
    {
        await _app.CreateCocoaPodsFeedAsync(Feed, configure: f => f.MaxPackageSizeMiB = 1);

        var archive = RandomNumberGenerator.GetBytes(1024 * 1024 + 1);
        using var response = await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0"), archive, "MyLib.tar");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("1 MiB", await response.Content.ReadAsStringAsync());
        Assert.Null(await _client.GetPodVersionsAsync(Feed, Pod));
    }

    [Fact]
    public async Task ConfiguredApiKeyIsNeededToPublish()
    {
        using var app = new PaGettoApplication(_output, null, dict => dict["Authentication:ApiKeys:0:Key"] = "secret-key");
        using var client = app.CreateClient();
        await app.CreateCocoaPodsFeedAsync(Feed);

        using var anonymous = await client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0"));

        client.UseBasicCredentials("ci", "wrong-key");
        using var wrong = await client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0"));

        client.UseBasicCredentials("ci", "secret-key");
        using var right = await client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0"));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("Basic realm=\"PaGetto\"", anonymous.Headers.WwwAuthenticate.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Created, right.StatusCode);
    }

    [Theory]
    // The shard of another pod, the wrong file name, an unknown version, and an invalid name.
    [InlineData("cocoapods/Specs/0/0/0/MyLib/1.0.0/MyLib.podspec.json")]
    [InlineData("cocoapods/Specs/2/f/4/MyLib/1.0.0/Other.podspec.json")]
    [InlineData("cocoapods/Specs/2/f/4/MyLib/9.9.9/MyLib.podspec.json")]
    [InlineData("cocoapods/Specs/2/f/4/MyLib/not-a-version/MyLib.podspec.json")]
    [InlineData("cocoapods/all_pods_versions_2_f.txt")]
    [InlineData("cocoapods/Specs.txt")]
    [InlineData("cocoapods/sources/MyLib/1.0.0/MyLib-1.0.0.zip")]
    [InlineData("cocoapods/sources/Other/1.0.0/Other-1.0.0.zip")]
    public async Task UnknownPodsAndOtherPathsAreNotFound(string path)
    {
        await _app.CreateCocoaPodsFeedAsync(Feed);
        Assert.Equal(new[] { "2", "f", "4" }, CocoaPodsPodName.GetShard(Pod));
        using (await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0")))
        {
        }

        using var response = await _client.GetAsync($"feeds/{Feed}/{path}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnchangedFilesAreNotModified()
    {
        // CocoaPods sends the ETags of its files back on pod repo update.
        await _app.CreateCocoaPodsFeedAsync(Feed);
        using (await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0")))
        {
        }

        foreach (var url in new[] { CocoaPodsTestClient.IndexUrl(Feed, Pod), CocoaPodsTestClient.PodspecUrl(Feed, Pod, "1.0.0") })
        {
            using var first = await _client.GetAsync(url);
            var etag = first.Headers.ETag;
            Assert.NotNull(etag);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.IfNoneMatch.Add(etag);
            using var second = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        }

        using (await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.1.0")))
        {
        }

        using var changed = new HttpRequestMessage(HttpMethod.Get, CocoaPodsTestClient.IndexUrl(Feed, Pod));
        changed.Headers.IfNoneMatch.Add((await _client.GetAsync(CocoaPodsTestClient.PodspecUrl(Feed, Pod, "1.0.0"))).Headers.ETag!);
        using var updated = await _client.SendAsync(changed);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
    }

    [Theory]
    [InlineData("GET", "v3/index.json")]
    [InlineData("PUT", "api/v2/package")]
    [InlineData("GET", "composer/packages.json")]
    [InlineData("GET", "hex/names")]
    [InlineData("GET", "upload")]
    public async Task OtherApisAreNotFoundOnACocoaPodsFeed(string method, string path)
    {
        await _app.CreateCocoaPodsFeedAsync(Feed);

        using var request = new HttpRequestMessage(new HttpMethod(method), $"feeds/{Feed}/{path}");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "cocoapods/CocoaPods-version.yml")]
    [InlineData("GET", "cocoapods/all_pods_versions_2_f_4.txt")]
    [InlineData("GET", "cocoapods/Specs/2/f/4/MyLib/1.0.0/MyLib.podspec.json")]
    [InlineData("GET", "cocoapods/sources/MyLib/1.0.0/MyLib-1.0.0.zip")]
    [InlineData("POST", "cocoapods/api/v1/pods")]
    public async Task CocoaPodsApiIsNotFoundOnOtherFeeds(string method, string path)
    {
        await _app.CreateFeedAsync("nuget");
        await _app.CreateComposerFeedAsync("php");

        foreach (var url in new[] { $"feeds/nuget/{path}", $"feeds/php/{path}", path })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task WebUiShowsTheFeedAndItsPods()
    {
        await _app.CreateCocoaPodsFeedAsync(Feed);
        var podspec = CocoaPodsTestClient.Podspec(Pod, "1.0.0", p =>
        {
            p["dependencies"] = new JsonObject { ["Alamofire"] = new JsonArray("~> 5.0") };
            p["summary"] = "<script>alert(1)</script>";
        });
        using (await _client.PublishAsync(Feed, podspec, CocoaPodsTestClient.SourceZip(), "MyLib.zip"))
        {
        }

        var index = await _client.GetStringAsync($"feeds/{Feed}/");
        Assert.Contains("CocoaPods feed", index);
        Assert.Contains("<span class=\"bgt-badge ms-1\">CocoaPods</span>", index);
        Assert.Contains(">MyLib<", index);
        Assert.Contains("pod &#x27;MyLib&#x27;, &#x27;1.0.0&#x27;", index);
        Assert.DoesNotContain("<script>alert(1)</script>", index);

        var link = Regex.Match(index, $"href=\"(/feeds/{Feed}/artifacts/[^\"]+)\"").Groups[1].Value;
        Assert.Contains("<h1>MyLib</h1>", await _client.GetStringAsync(WebUtility.HtmlDecode(link)));

        var details = await _client.GetStringAsync($"feeds/{Feed}/artifacts/MyLib");
        Assert.Contains("<h1>MyLib</h1>", details);
        Assert.Contains("iOS 12.0", details);
        Assert.Contains("Swift 5.0", details);
        Assert.Contains("Alamofire", details);
        Assert.Contains("MIT", details);
        Assert.Contains("https://example.com/MyLib", details);
        Assert.Contains($"http://localhost/feeds/{Feed}/cocoapods/", details);
        Assert.Contains($"/feeds/{Feed}/cocoapods/sources/MyLib/1.0.0/MyLib-1.0.0.zip", details);
        Assert.Contains("Download source", details);

        var connect = await _client.GetStringAsync($"feeds/{Feed}/connect");
        Assert.Contains($"http://localhost/feeds/{Feed}/cocoapods/", connect);
        Assert.Contains("machine localhost", connect);
        Assert.Contains("pod ipc spec", connect);
        Assert.Contains("api/v1/pods", connect);
        Assert.Contains("https://cdn.cocoapods.org/", connect);
    }

    private async Task<ArtifactVersion> GetVersionAsync(string version)
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return await context.ArtifactVersions
            .AsNoTracking()
            .Include(v => v.Artifact)
            .SingleAsync(v => v.Version == version);
    }

    private async Task SetListedAsync(string version, bool listed)
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        var stored = await context.ArtifactVersions.SingleAsync(v => v.Version == version);
        stored.Listed = listed;
        await context.SaveChangesAsync(default);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
