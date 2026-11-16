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
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Tests.Support;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The Composer repository of a Composer feed, in Legacy mode without credentials (open access).
/// </summary>
public class ComposerRepositoryIntegrationTests : IDisposable
{
    private const string Feed = "php";
    private const string Package = "acme/hello";

    private readonly ITestOutputHelper _output;
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public ComposerRepositoryIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task RootPointsComposerAtTheMetadata()
    {
        await _app.CreateComposerFeedAsync(Feed);

        var root = JsonNode.Parse(await _client.GetStringAsync($"feeds/{Feed}/composer/packages.json"))!;

        Assert.Empty(root["packages"]!.AsArray());
        Assert.Equal($"http://localhost/feeds/{Feed}/composer/p2/%package%.json", root["metadata-url"]!.GetValue<string>());
    }

    [Fact]
    public async Task UploadedPackageIsServedToComposer()
    {
        await _app.CreateComposerFeedAsync(Feed);
        var zip = ComposerZipBuilder.Create("Acme/Hello", readme: "# Hello");
        var sha1 = Convert.ToHexStringLower(SHA1.HashData(zip));

        using var upload = await _client.UploadPackageAsync(Feed, Package, "v1.0.0", zip);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        var version = Assert.Single(await _client.GetVersionsAsync(Feed, Package))!.AsObject();
        Assert.Equal(Package, version["name"]!.GetValue<string>());
        Assert.Equal("v1.0.0", version["version"]!.GetValue<string>());
        Assert.Equal("1.0.0.0", version["version_normalized"]!.GetValue<string>());
        Assert.Equal("2020-01-01T00:00:00+00:00", version["time"]!.GetValue<string>());
        Assert.Equal(">=8.1", version["require"]!["php"]!.GetValue<string>());
        Assert.Equal("src/", version["autoload"]!["psr-4"]!["Acme\\Hello\\"]!.GetValue<string>());

        var dist = version["dist"]!;
        Assert.Equal("zip", dist["type"]!.GetValue<string>());
        Assert.Equal(sha1, dist["reference"]!.GetValue<string>());
        Assert.Equal(sha1, dist["shasum"]!.GetValue<string>());
        Assert.Equal($"http://localhost/feeds/{Feed}/composer/dists/{Package}/v1.0.0/{sha1}.zip", dist["url"]!.GetValue<string>());

        using var download = await _client.GetAsync(dist["url"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(zip, await download.Content.ReadAsByteArrayAsync());

        var stored = await GetVersionAsync("v1.0.0");
        Assert.Equal(Package, stored.Artifact.Name);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(zip)), stored.ContentHash);
        Assert.Equal("A test package", stored.Description);
        Assert.False(stored.IsPrerelease);
        Assert.True(stored.HasReadme);
        Assert.Equal(1, stored.Downloads);
    }

    [Fact]
    public async Task PackageInAFolderOfTheZipIsAccepted()
    {
        await _app.CreateComposerFeedAsync(Feed);

        using var upload = await _client.UploadPackageAsync(Feed, Package, "1.0.0", ComposerZipBuilder.Create(Package, folder: "hello-1.0.0"));

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
    }

    [Fact]
    public async Task DevelopmentVersionsAreInTheDevFile()
    {
        await _app.CreateComposerFeedAsync(Feed);
        foreach (var version in new[] { "1.0.0", "dev-main", "2.0.0-beta1", "1.x-dev", "1.2.0" })
        {
            using var upload = await _client.UploadPackageAsync(Feed, Package, version);
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        var tagged = await _client.GetVersionsAsync(Feed, Package);
        var dev = await _client.GetVersionsAsync(Feed, Package, dev: true);

        // Newest first, as Packagist lists them.
        Assert.Equal(new[] { "2.0.0-beta1", "1.2.0", "1.0.0" }, tagged.Select(v => v!["version"]!.GetValue<string>()));
        Assert.Equal(new[] { "1.x-dev", "dev-main" }, dev.Select(v => v!["version"]!.GetValue<string>()));
        Assert.Equal("1.9999999.9999999.9999999-dev", dev.FindVersion("1.x-dev")["version_normalized"]!.GetValue<string>());

        Assert.True((await GetVersionAsync("2.0.0-beta1")).IsPrerelease);
        Assert.True((await GetVersionAsync("dev-main")).IsPrerelease);
    }

    [Fact]
    public async Task UnlistedVersionIsLeftOutButStillServed()
    {
        await _app.CreateComposerFeedAsync(Feed);
        using (await _client.UploadPackageAsync(Feed, Package, "1.0.0"))
        {
        }

        using (await _client.UploadPackageAsync(Feed, Package, "1.1.0"))
        {
        }

        await SetListedAsync("1.1.0", listed: false);

        var versions = await _client.GetVersionsAsync(Feed, Package);
        Assert.Equal("1.0.0", Assert.Single(versions)!["version"]!.GetValue<string>());

        // Lock files keep the URL of the version.
        using var zip = await _client.GetAsync($"feeds/{Feed}/composer/dists/{Package}/1.1.0/any.zip");
        Assert.Equal(HttpStatusCode.OK, zip.StatusCode);
    }

    [Fact]
    public async Task VersionsAreMatchedByTheirNormalizedForm()
    {
        await _app.CreateComposerFeedAsync(Feed);
        using (await _client.UploadPackageAsync(Feed, Package, "v1.0.0"))
        {
        }

        using var again = await _client.UploadPackageAsync(Feed, Package, "1.0");
        using var download = await _client.GetAsync($"feeds/{Feed}/composer/dists/{Package}/1.0.0.0/x.zip");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("Version v1.0.0 of package acme/hello already exists", await again.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Fact]
    public async Task OverwritingFeedReplacesTheVersionWithANewReference()
    {
        await _app.CreateComposerFeedAsync(Feed, configure: f => f.AllowPackageOverwrites = PackageOverwriteAllowed.True);
        using (await _client.UploadPackageAsync(Feed, Package, "1.0.0"))
        {
        }

        var before = (await _client.GetVersionsAsync(Feed, Package)).FindVersion("1.0.0")["dist"]!["url"]!.GetValue<string>();

        var replacement = ComposerZipBuilder.Create(Package, readme: "changed");
        using var response = await _client.UploadPackageAsync(Feed, Package, "1.0.0", replacement);

        var after = (await _client.GetVersionsAsync(Feed, Package)).FindVersion("1.0.0")["dist"]!["url"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotEqual(before, after);
        Assert.Equal(replacement, await _client.GetByteArrayAsync(after));
    }

    [Fact]
    public async Task DevelopmentVersionIsAlwaysReplaced()
    {
        await _app.CreateComposerFeedAsync(Feed);
        using (var first = await _client.UploadPackageAsync(Feed, Package, "dev-main"))
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        var replacement = ComposerZipBuilder.Create(Package, readme: "newer commit");
        using var second = await _client.UploadPackageAsync(Feed, Package, "dev-main", replacement);
        using var release = await _client.UploadPackageAsync(Feed, Package, "1.0.0");
        using var releaseAgain = await _client.UploadPackageAsync(Feed, Package, "1.0.0");

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var dist = (await _client.GetVersionsAsync(Feed, Package, dev: true)).FindVersion("dev-main")["dist"]!;
        Assert.Equal(Convert.ToHexStringLower(SHA1.HashData(replacement)), dist["reference"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict, releaseAgain.StatusCode);
    }

    [Theory]
    [InlineData("acme/-hello", "1.0.0", "not a valid package name")]
    [InlineData(Package, "1.0.0-meh", "not a version Composer accepts")]
    [InlineData(Package, "dev-feature%2Ffoo", "not a version Composer accepts")]
    public async Task InvalidNameOrVersionIsRejectedWithAMessage(string name, string version, string message)
    {
        await _app.CreateComposerFeedAsync(Feed);

        using var response = await _client.UploadPackageAsync(Feed, name, version, ComposerZipBuilder.Create(Package));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync());
    }

    public static IEnumerable<object[]> InvalidZips()
    {
        yield return [ComposerZipBuilder.Create("acme/other"), "names the package acme/other, not acme/hello"];
        yield return [ComposerZipBuilder.Create(Package, composerJson: "{}"), "names the package (none)"];
        yield return [ComposerZipBuilder.Create(Package, composerJson: ComposerZipBuilder.ComposerJson(Package, j => j["version"] = "2.0.0")), "has the version 2.0.0, not 1.0.0"];
        yield return [ComposerZipBuilder.CreateZip(new Dictionary<string, string> { ["src/Hello.php"] = "<?php\n" }), "no composer.json"];
        yield return [new byte[] { 1, 2, 3 }, "not a valid zip"];
    }

    [Theory]
    [MemberData(nameof(InvalidZips))]
    public async Task InvalidZipIsRejectedWithAMessage(byte[] zip, string message)
    {
        await _app.CreateComposerFeedAsync(Feed);

        using var response = await _client.UploadPackageAsync(Feed, Package, "1.0.0", zip);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task VersionOfComposerJsonMayBeWrittenDifferently()
    {
        await _app.CreateComposerFeedAsync(Feed);
        var zip = ComposerZipBuilder.Create(Package, composerJson: ComposerZipBuilder.ComposerJson(Package, j => j["version"] = "v1.0"));

        using var response = await _client.UploadPackageAsync(Feed, Package, "1.0.0", zip);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("1.0.0", (await _client.GetVersionsAsync(Feed, Package)).Single()!["version"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReadOnlyFeedRefusesUploads()
    {
        await _app.CreateComposerFeedAsync(Feed, configure: f => f.IsReadOnlyMode = true);

        using var response = await _client.UploadPackageAsync(Feed, Package, "1.0.0");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("read-only", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ZipLargerThanTheFeedLimitIsRejected()
    {
        await _app.CreateComposerFeedAsync(Feed, configure: f => f.MaxPackageSizeMiB = 1);

        // Random text doesn't compress, so the zip stays above 1 MiB.
        var readme = Convert.ToBase64String(RandomNumberGenerator.GetBytes(1024 * 1024));
        using var response = await _client.UploadPackageAsync(Feed, Package, "1.0.0", ComposerZipBuilder.Create(Package, readme: readme));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("1 MiB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConfiguredApiKeyIsNeededToUpload()
    {
        using var app = new PaGettoApplication(_output, null, dict => dict["Authentication:ApiKeys:0:Key"] = "secret-key");
        using var client = app.CreateClient();
        await app.CreateComposerFeedAsync(Feed);

        using var anonymous = await client.UploadPackageAsync(Feed, Package, "1.0.0");

        client.UseBasicCredentials("ci", "wrong-key");
        using var wrong = await client.UploadPackageAsync(Feed, Package, "1.0.0");

        client.UseBasicCredentials("ci", "secret-key");
        using var right = await client.UploadPackageAsync(Feed, Package, "1.0.0");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("Basic realm=\"PaGetto\"", anonymous.Headers.WwwAuthenticate.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Created, right.StatusCode);
    }

    [Theory]
    [InlineData("composer/p2/acme/hello.json")]
    [InlineData("composer/p2/acme/hello~dev.json")]
    [InlineData("composer/dists/acme/hello/1.0.0/x.zip")]
    [InlineData("composer/p2/acme/hello.txt")]
    [InlineData("composer/p2/acme/-hello.json")]
    [InlineData("composer/dists/acme/hello/1.0.0/x.tar")]
    [InlineData("composer/dists/acme/hello/not-a-version/x.zip")]
    public async Task UnknownPackagesAndOtherPathsAreNotFound(string path)
    {
        await _app.CreateComposerFeedAsync(Feed);
        using (await _client.UploadPackageAsync(Feed, "acme/other", "1.0.0", ComposerZipBuilder.Create("acme/other")))
        {
        }

        using var response = await _client.GetAsync($"feeds/{Feed}/{path}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PackageWithOnlyDevelopmentVersionsHasNoTaggedMetadata()
    {
        await _app.CreateComposerFeedAsync(Feed);
        using (await _client.UploadPackageAsync(Feed, Package, "dev-main"))
        {
        }

        using var tagged = await _client.GetAsync(ComposerTestClient.MetadataUrl(Feed, Package));
        using var dev = await _client.GetAsync(ComposerTestClient.MetadataUrl(Feed, Package, dev: true));

        Assert.Equal(HttpStatusCode.NotFound, tagged.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dev.StatusCode);
    }

    [Theory]
    [InlineData("GET", "v3/index.json")]
    [InlineData("PUT", "api/v2/package")]
    [InlineData("GET", "api/packages/hello")]
    [InlineData("GET", "go/example.com/tool/@v/list")]
    [InlineData("GET", "hex/names")]
    [InlineData("GET", "upload")]
    public async Task OtherApisAreNotFoundOnAComposerFeed(string method, string path)
    {
        await _app.CreateComposerFeedAsync(Feed);

        using var request = new HttpRequestMessage(new HttpMethod(method), $"feeds/{Feed}/{path}");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "composer/packages.json")]
    [InlineData("GET", "composer/p2/acme/hello.json")]
    [InlineData("GET", "composer/dists/acme/hello/1.0.0/x.zip")]
    [InlineData("PUT", "composer/packages/upload/acme/hello/1.0.0")]
    public async Task ComposerApiIsNotFoundOnOtherFeeds(string method, string path)
    {
        await _app.CreateFeedAsync("nuget");
        await _app.CreateGoFeedAsync("golang");

        foreach (var url in new[] { $"feeds/nuget/{path}", $"feeds/golang/{path}", path })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task WebUiShowsTheFeedAndItsPackages()
    {
        await _app.CreateComposerFeedAsync(Feed);

        var composerJson = ComposerZipBuilder.ComposerJson(Package, j =>
        {
            j["require"]!["psr/log"] = "^3.0";
            j["require"]!["ext-json"] = "*";
            j["keywords"] = new JsonArray("greeting");
            j["support"] = new JsonObject { ["source"] = "https://github.com/acme/hello" };
        });
        var zip = ComposerZipBuilder.Create(Package, readme: "# Hello from the readme\n\n<script>alert(1)</script>", composerJson: composerJson);
        using (await _client.UploadPackageAsync(Feed, Package, "v1.0.0", zip))
        {
        }

        var index = await _client.GetStringAsync($"feeds/{Feed}/");
        Assert.Contains("PHP feed", index);
        Assert.Contains("<span class=\"bgt-badge ms-1\">PHP</span>", index);
        Assert.Contains(Package, index);
        Assert.Contains($"composer require {Package}:v1.0.0", index);
        Assert.Contains(">v1.0.0<", index);
        Assert.DoesNotContain("vv1.0.0", index);

        var link = Regex.Match(index, $"href=\"(/feeds/{Feed}/artifacts/[^\"]+)\"").Groups[1].Value;
        Assert.Contains($"<h1>{Package}</h1>", await _client.GetStringAsync(WebUtility.HtmlDecode(link)));

        var details = await _client.GetStringAsync($"feeds/{Feed}/artifacts/{Uri.EscapeDataString(Package)}");
        Assert.Contains($"<h1>{Package}</h1>", details);
        Assert.Contains("Hello from the readme", details);
        Assert.DoesNotContain("<script>alert(1)</script>", details);
        Assert.Contains("PHP &gt;=8.1", details);
        Assert.Contains("psr/log", details);
        Assert.DoesNotContain("ext-json", details);
        Assert.Contains("MIT", details);
        Assert.Contains("greeting", details);
        Assert.Contains("https://github.com/acme/hello", details);
        Assert.Contains($"composer require {Package}:v1.0.0", details);
        Assert.Contains($"/feeds/{Feed}/composer/dists/{Package}/v1.0.0/v1.0.0.zip", details);

        var connect = await _client.GetStringAsync($"feeds/{Feed}/connect");
        Assert.Contains($"http://localhost/feeds/{Feed}/composer", connect);
        Assert.Contains("http-basic", connect);
        Assert.Contains("secure-http", connect);
        Assert.Contains("packages/upload/vendor/package/1.0.0", connect);
    }

    [Fact]
    public async Task ConnectPageNamesTheHostWithItsPortForAuthJson()
    {
        // Composer matches http-basic credentials by host and port when the port is not the default.
        await _app.CreateComposerFeedAsync(Feed);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"feeds/{Feed}/connect");
        request.Headers.Host = "localhost:5000";
        using var response = await _client.SendAsync(request);

        Assert.Contains("http-basic.localhost:5000 ", await response.Content.ReadAsStringAsync());
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
