using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Tests.Support;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The repository and API of a Hex feed, in Legacy mode without credentials (open access).
/// </summary>
public class HexRepositoryIntegrationTests : IDisposable
{
    private const string Feed = "hex";

    private readonly ITestOutputHelper _output;
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public HexRepositoryIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task PublishedPackageIsInTheSignedRegistryAndDownloadable()
    {
        await _app.CreateHexFeedAsync(Feed);

        var requirements = $"[{HexTarballBuilder.Requirement("jason", "~> 1.4")},{HexTarballBuilder.Requirement("own", "~> 2.0", repository: Feed, optional: true)}]";
        var tarball = HexTarballBuilder.Create("demo", "1.0.0", requirements: requirements);

        using var publish = await _client.PublishHexAsync(Feed, "demo", tarball);

        Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        var body = await publish.ReadHexTermAsync();
        Assert.Equal("1.0.0", body["version"]);
        Assert.Equal($"http://localhost/feeds/{Feed}/artifacts/demo/1.0.0", body["html_url"]);

        using (await _client.PublishHexAsync(Feed, "demo", "1.1.0-rc.1"))
        {
        }

        var releases = await _client.GetReleasesAsync(Feed, "demo");
        Assert.Equal(new[] { "1.0.0", "1.1.0-rc.1" }, releases.Select(r => r.Version));

        var release = releases[0];
        Assert.Equal(SHA256.HashData(tarball), release.OuterChecksum);
        Assert.Equal(32, release.InnerChecksum.Length);
        Assert.Null(release.Retirement);
        Assert.NotNull(release.PublishedAt);

        // A dependency in the feed's own repository is listed without one.
        Assert.Collection(
            release.Requirements,
            r =>
            {
                Assert.Equal("jason", r.Name);
                Assert.Equal("~> 1.4", r.Requirement);
                Assert.Equal("hexpm", r.Repository);
            },
            r =>
            {
                Assert.Equal("own", r.Name);
                Assert.True(r.Optional);
                Assert.Null(r.Repository);
            });

        using var download = await _client.GetAsync(HexTestClient.TarballUrl(Feed, "demo", "1.0.0"));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(tarball, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, await GetDownloadsAsync("demo", "1.0.0"));

        // Rebar3 only takes a tarball with an ETag, and sends it back to check its cache.
        var etag = download.Headers.ETag;
        Assert.NotNull(etag);
        using var cached = new HttpRequestMessage(HttpMethod.Get, HexTestClient.TarballUrl(Feed, "demo", "1.0.0"));
        cached.Headers.IfNoneMatch.Add(etag);
        using var notModified = await _client.SendAsync(cached);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
    }

    [Fact]
    public async Task RegistryResourcesHaveETags()
    {
        await _app.CreateHexFeedAsync(Feed);
        using (await _client.PublishHexAsync(Feed, "demo", "1.0.0"))
        {
        }

        using var first = await _client.GetAsync($"{HexTestClient.RepositoryUrl(Feed)}/packages/demo");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{HexTestClient.RepositoryUrl(Feed)}/packages/demo");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag);
        using var second = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task NamesAndVersionsListThePackagesOfTheFeed()
    {
        await _app.CreateHexFeedAsync(Feed);
        using (await _client.PublishHexAsync(Feed, "alpha", "1.0.0"))
        {
        }

        using (await _client.PublishHexAsync(Feed, "beta", "2.0.0"))
        {
        }

        var names = await _client.GetRegistryPayloadAsync(Feed, "names");
        var versions = await _client.GetRegistryPayloadAsync(Feed, "versions");

        Assert.True(Contains(names, "alpha") && Contains(names, "beta") && Contains(names, Feed));
        Assert.True(Contains(versions, "alpha") && Contains(versions, "1.0.0") && Contains(versions, "2.0.0"));
    }

    [Fact]
    public async Task PublicKeyIsTheSameEveryTime()
    {
        await _app.CreateHexFeedAsync(Feed);
        await _app.CreateHexFeedAsync("other");

        var first = await _client.GetStringAsync($"{HexTestClient.RepositoryUrl(Feed)}/public_key");
        var second = await _client.GetStringAsync($"{HexTestClient.RepositoryUrl(Feed)}/public_key");
        var other = await _client.GetStringAsync($"{HexTestClient.RepositoryUrl("other")}/public_key");

        Assert.StartsWith("-----BEGIN PUBLIC KEY-----", first);
        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public async Task FeedCreatedWithTheSlugOfADeletedOneHasAKeyOfItsOwn()
    {
        var deleted = await _app.CreateHexFeedAsync(Feed);
        var first = await _client.GetStringAsync($"{HexTestClient.RepositoryUrl(Feed)}/public_key");

        using (var scope = _app.Services.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IFeedService>().DeleteFeedAsync(deleted.Id, default));
        }

        await _app.CreateHexFeedAsync(Feed);
        var second = await _client.GetStringAsync($"{HexTestClient.RepositoryUrl(Feed)}/public_key");

        Assert.StartsWith("-----BEGIN PUBLIC KEY-----", second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task UnknownPackageIsNotFound()
    {
        await _app.CreateHexFeedAsync(Feed);

        using var registry = await _client.GetAsync($"{HexTestClient.RepositoryUrl(Feed)}/packages/unknown");
        using var download = await _client.GetAsync(HexTestClient.TarballUrl(Feed, "unknown", "1.0.0"));
        using var badName = await _client.GetAsync($"{HexTestClient.RepositoryUrl(Feed)}/tarballs/unknown.tar");

        Assert.Equal(HttpStatusCode.NotFound, registry.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, badName.StatusCode);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+build.2")]
    public async Task PublishingAnExistingVersionIsRejected(string version)
    {
        await _app.CreateHexFeedAsync(Feed);
        using (await _client.PublishHexAsync(Feed, "demo", "1.0.0"))
        {
        }

        using var response = await _client.PublishHexAsync(Feed, "demo", version);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("already exists", await response.ReadHexErrorAsync());
    }

    [Fact]
    public async Task OverwritingFeedReplacesTheVersionWhenAskedTo()
    {
        await _app.CreateHexFeedAsync(Feed, configure: f => f.AllowPackageOverwrites = PackageOverwriteAllowed.True);
        using (await _client.PublishHexAsync(Feed, "demo", "1.0.0"))
        {
        }

        var replacement = HexTarballBuilder.Create("demo", "1.0.0", readme: "changed");
        using var withoutReplace = await _client.PublishHexAsync(Feed, "demo", replacement);
        using var withReplace = await _client.PublishHexAsync(Feed, "demo", replacement, replace: true);

        Assert.Equal(HttpStatusCode.Conflict, withoutReplace.StatusCode);
        Assert.Contains("--replace", await withoutReplace.ReadHexErrorAsync());
        Assert.Equal(HttpStatusCode.Created, withReplace.StatusCode);
        Assert.Equal(SHA256.HashData(replacement), Assert.Single(await _client.GetReleasesAsync(Feed, "demo")).OuterChecksum);
    }

    [Fact]
    public async Task InvalidPackageIsRejectedWithAMessage()
    {
        await _app.CreateHexFeedAsync(Feed);

        using var garbage = await _client.PublishHexAsync(Feed, "demo", Encoding.UTF8.GetBytes("not a tarball"));
        using var badName = await _client.PublishHexAsync(Feed, "Demo", HexTarballBuilder.Create("Demo", "1.0.0"));
        using var badVersion = await _client.PublishHexAsync(Feed, "demo", HexTarballBuilder.Create("demo", "1.0"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, garbage.StatusCode);
        Assert.Contains("tar archive", await garbage.ReadHexErrorAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badName.StatusCode);
        Assert.Contains("package name", await badName.ReadHexErrorAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badVersion.StatusCode);
        Assert.Contains("semantic version", await badVersion.ReadHexErrorAsync());
    }

    [Fact]
    public async Task RetiringMarksTheVersionInTheRegistryButKeepsItDownloadable()
    {
        await _app.CreateHexFeedAsync(Feed);
        using (await _client.PublishHexAsync(Feed, "demo", "1.0.0"))
        {
        }

        using var retire = await _client.RetireHexAsync(Feed, "demo", "1.0.0", "security", "Use 1.0.1");

        Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);
        var retirement = Assert.Single(await _client.GetReleasesAsync(Feed, "demo")).Retirement;
        Assert.Equal("security", retirement.Reason);
        Assert.Equal("Use 1.0.1", retirement.Message);

        using (var download = await _client.GetAsync(HexTestClient.TarballUrl(Feed, "demo", "1.0.0")))
        {
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        }

        using var unretire = await _client.UnretireHexAsync(Feed, "demo", "1.0.0");

        Assert.Equal(HttpStatusCode.NoContent, unretire.StatusCode);
        Assert.Null(Assert.Single(await _client.GetReleasesAsync(Feed, "demo")).Retirement);

        using var unknown = await _client.RetireHexAsync(Feed, "demo", "9.9.9", "other", "Gone");
        using var badReason = await _client.RetireHexAsync(Feed, "demo", "1.0.0", "bored", "No");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badReason.StatusCode);
        Assert.Contains("reason", await badReason.ReadHexErrorAsync());
    }

    [Fact]
    public async Task ReadOnlyFeedRefusesToPublishAndRetire()
    {
        await _app.CreateHexFeedAsync(Feed, configure: f => f.IsReadOnlyMode = true);

        using var publish = await _client.PublishHexAsync(Feed, "demo", "1.0.0");
        using var retire = await _client.RetireHexAsync(Feed, "demo", "1.0.0", "other", "No");

        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
        Assert.Contains("read-only", await publish.ReadHexErrorAsync());
        Assert.Equal(HttpStatusCode.Forbidden, retire.StatusCode);
    }

    [Fact]
    public async Task PackageLargerThanTheFeedLimitIsRejected()
    {
        await _app.CreateHexFeedAsync(Feed, configure: f => f.MaxPackageSizeMiB = 1);

        // Random text doesn't compress, so the tarball stays above 1 MiB.
        var readme = Convert.ToBase64String(RandomNumberGenerator.GetBytes(1024 * 1024));
        using var response = await _client.PublishHexAsync(Feed, "demo", HexTarballBuilder.Create("demo", "1.0.0", readme));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("1 MiB", await response.ReadHexErrorAsync());
    }

    [Fact]
    public async Task AnswersWhatMixHexPublishAsksBeforePublishing()
    {
        await _app.CreateHexFeedAsync(Feed);
        using (await _client.PublishHexAsync(Feed, "demo", "1.0.0"))
        {
        }

        using var hexpm = await _client.GetAsync($"{HexTestClient.RepositoryUrl(Feed)}/api/repos/hexpm/packages/demo");
        using var me = await _client.GetAsync($"{HexTestClient.RepositoryUrl(Feed)}/api/users/me");
        using var package = await _client.GetAsync($"{HexTestClient.RepositoryUrl(Feed)}/api/packages/demo");

        Assert.Equal(HttpStatusCode.NotFound, hexpm.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Empty((List<object>)(await me.ReadHexTermAsync())["organizations"]);
        Assert.Equal(HttpStatusCode.OK, package.StatusCode);
        Assert.Equal("1.0.0", ((Dictionary<string, object>)Assert.Single((List<object>)(await package.ReadHexTermAsync())["releases"]))["version"]);
    }

    [Fact]
    public async Task OlderClientsPublishToo()
    {
        await _app.CreateHexFeedAsync(Feed);

        using var content = new ByteArrayContent(HexTarballBuilder.Create("demo", "1.0.0"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await _client.PostAsync($"{HexTestClient.RepositoryUrl(Feed)}/api/publish", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ConfiguredApiKeyIsNeededToPublish()
    {
        using var app = new PaGettoApplication(_output, null, dict => dict["Authentication:ApiKeys:0:Key"] = "secret-key");
        using var client = app.CreateClient();
        await app.CreateHexFeedAsync(Feed);

        using var anonymous = await client.PublishHexAsync(Feed, "demo", "1.0.0");

        client.UseHexKey("wrong-key");
        using var wrong = await client.PublishHexAsync(Feed, "demo", "1.0.0");

        client.UseHexKey("secret-key");
        using var right = await client.PublishHexAsync(Feed, "demo", "1.0.0");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Created, right.StatusCode);
    }

    [Theory]
    [InlineData("GET", "v3/index.json")]
    [InlineData("PUT", "api/v2/package")]
    [InlineData("GET", "cargo/index/config.json")]
    [InlineData("GET", "npm/demo")]
    [InlineData("GET", "upload")]
    public async Task OtherApisAreNotFoundOnAHexFeed(string method, string path)
    {
        await _app.CreateHexFeedAsync(Feed);

        using var request = new HttpRequestMessage(new HttpMethod(method), $"feeds/{Feed}/{path}");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("hex/names")]
    [InlineData("hex/packages/demo")]
    [InlineData("hex/public_key")]
    [InlineData("hex/tarballs/demo-1.0.0.tar")]
    public async Task HexRepositoryIsNotFoundOnANuGetFeed(string path)
    {
        await _app.CreateFeedAsync("nuget");

        using var named = await _client.GetAsync($"feeds/nuget/{path}");
        using var defaultFeed = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, named.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, defaultFeed.StatusCode);
    }

    [Fact]
    public async Task WebUiShowsTheFeedAndItsPackages()
    {
        await _app.CreateHexFeedAsync(Feed);

        var tarball = HexTarballBuilder.Create(
            "demo",
            "1.0.0",
            readme: "# Hello from the readme\n\n<script>alert(1)</script>",
            requirements: $"[{HexTarballBuilder.Requirement("jason", "~> 1.4")}]");
        using (await _client.PublishHexAsync(Feed, "demo", tarball))
        {
        }

        var index = await _client.GetStringAsync($"feeds/{Feed}/");
        Assert.Contains("Hex feed", index);
        Assert.Contains("<span class=\"bgt-badge ms-1\">Hex</span>", index);
        Assert.Contains("demo", index);
        Assert.Contains("A test package.", index);
        Assert.Contains($"{{:demo, &quot;~&gt; 1.0.0&quot;, repo: &quot;{Feed}&quot;}}", index);

        var details = await _client.GetStringAsync($"feeds/{Feed}/artifacts/demo");
        Assert.Contains("<h1>demo</h1>", details);
        Assert.Contains("Hello from the readme", details);
        Assert.DoesNotContain("<script>alert(1)</script>", details);
        Assert.Contains("https://github.com/example/demo", details);
        Assert.Contains("Elixir ~&gt; 1.15", details);
        Assert.Contains("jason", details);
        Assert.Contains("MIT", details);
        Assert.Contains("rebar.config", details);
        Assert.Contains($"/feeds/{Feed}/hex/tarballs/demo-1.0.0.tar", details);

        var connect = await _client.GetStringAsync($"feeds/{Feed}/connect");
        Assert.Contains($"http://localhost/feeds/{Feed}/hex", connect);
        Assert.Contains($"mix hex.repo add {Feed}", connect);
        Assert.Contains("SHA256:", connect);
        Assert.Contains("HEX_API_URL", connect);
    }

    private static bool Contains(byte[] payload, string text)
    {
        return payload.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)) >= 0;
    }

    private async Task<long> GetDownloadsAsync(string name, string version)
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return await context.ArtifactVersions
            .Where(v => v.Artifact.Name == name && v.Version == version)
            .Select(v => v.Downloads)
            .SingleAsync();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
