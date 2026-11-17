using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The Swift package registry API of a Swift feed, in Legacy mode without credentials (open access).
/// </summary>
public class SwiftRegistryIntegrationTests : IDisposable
{
    private const string Feed = "swift";
    private const string Package = "mona.LinkedList";

    private readonly ITestOutputHelper _output;
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public SwiftRegistryIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task PublishedReleaseIsServedToSwiftPackageManager()
    {
        await _app.CreateSwiftFeedAsync(Feed);
        var archive = SwiftTestClient.SourceArchive(readme: "# LinkedList");
        var checksum = Convert.ToHexStringLower(SHA256.HashData(archive));

        using var publish = await _client.PublishAsync(Feed, Package, "1.0.0", archive, SwiftTestClient.Metadata());

        Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        Assert.Equal("1", publish.Headers.GetValues("Content-Version").Single());
        Assert.Equal($"http://localhost/feeds/{Feed}/swift/mona/LinkedList/1.0.0", publish.Headers.Location?.ToString());

        using var list = await GetAsync(SwiftTestClient.ReleaseUrl(Feed, Package));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal("application/json", list.Content.Headers.ContentType?.MediaType);
        Assert.Equal("1", list.Headers.GetValues("Content-Version").Single());
        Assert.Equal(
            """{"releases":{"1.0.0":{"url":"http://localhost/feeds/swift/swift/mona/LinkedList/1.0.0"}}}""",
            await list.Content.ReadAsStringAsync());

        var release = await _client.GetReleaseAsync(Feed, Package, "1.0.0");
        Assert.Equal(
            $$"""{"id":"mona.LinkedList","version":"1.0.0","resources":[{"name":"source-archive","type":"application/zip","checksum":"{{checksum}}"}],"metadata":{{SwiftTestClient.Metadata()}},"publishedAt":"2020-01-01T00:00:00Z"}""",
            release.ToJsonString());

        using var manifest = await GetAsync(SwiftTestClient.ReleaseUrl(Feed, Package, "1.0.0") + "/Package.swift");
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Equal("text/x-swift", manifest.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment; filename=\"Package.swift\"", manifest.Content.Headers.ContentDisposition?.ToString());
        Assert.Equal(SwiftTestClient.PackageSwift(), await manifest.Content.ReadAsStringAsync());
        Assert.False(manifest.Headers.Contains("Link"));

        using var zip = await GetAsync(SwiftTestClient.ReleaseUrl(Feed, Package, "1.0.0") + ".zip");
        Assert.Equal(HttpStatusCode.OK, zip.StatusCode);
        Assert.Equal("application/zip", zip.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment; filename=\"LinkedList-1.0.0.zip\"", zip.Content.Headers.ContentDisposition?.ToString());
        Assert.Equal("sha-256=" + Convert.ToBase64String(SHA256.HashData(archive)), zip.Headers.GetValues("Digest").Single());
        Assert.Equal(archive, await zip.Content.ReadAsByteArrayAsync());

        var stored = await GetVersionAsync("1.0.0");
        Assert.Equal("mona.linkedlist", stored.Artifact.Name);
        Assert.Equal("A linked list", stored.Description);
        Assert.Equal(checksum, stored.ContentHash);
        Assert.Equal(archive.Length, stored.Size);
        Assert.True(stored.HasReadme);
        Assert.False(stored.IsPrerelease);
        Assert.Null(stored.CachedFrom);
        Assert.Equal(1, stored.Downloads);
    }

    [Fact]
    public async Task JsonSuffixesAnswerTheSame()
    {
        await _app.CreateSwiftFeedAsync(Feed);
        await PublishAsync("1.0.0");

        Assert.Equal(
            await _client.GetStringAsync(SwiftTestClient.ReleaseUrl(Feed, Package)),
            await _client.GetStringAsync(SwiftTestClient.ReleaseUrl(Feed, Package) + ".json"));
        Assert.Equal(
            await _client.GetStringAsync(SwiftTestClient.ReleaseUrl(Feed, Package, "1.0.0")),
            await _client.GetStringAsync(SwiftTestClient.ReleaseUrl(Feed, Package, "1.0.0") + ".json"));
    }

    [Fact]
    public async Task VersionSpecificManifestsAreLinkedAndServed()
    {
        await _app.CreateSwiftFeedAsync(Feed);
        var swift6 = SwiftTestClient.PackageSwift(toolsVersion: "6.0");
        await PublishAsync("1.0.0", SwiftTestClient.SourceArchive(extraFiles: new Dictionary<string, string>
        {
            ["Package@swift-6.swift"] = swift6,
            ["Package@swift-5.7.swift"] = SwiftTestClient.PackageSwift(toolsVersion: "5.7"),
        }));
        var manifestUrl = SwiftTestClient.ReleaseUrl(Feed, Package, "1.0.0") + "/Package.swift";

        using var manifest = await GetAsync(manifestUrl);
        Assert.Equal(
            $"<http://localhost/{manifestUrl}?swift-version=5.7>; rel=\"alternate\"; filename=\"Package@swift-5.7.swift\"; swift-tools-version=\"5.7\", " +
            $"<http://localhost/{manifestUrl}?swift-version=6>; rel=\"alternate\"; filename=\"Package@swift-6.swift\"; swift-tools-version=\"6.0\"",
            manifest.Headers.GetValues("Link").Single());

        // Swift Package Manager asks with the tools version the alternate declares.
        using var alternate = await GetAsync(manifestUrl + "?swift-version=6.0.0");
        Assert.Equal(HttpStatusCode.OK, alternate.StatusCode);
        Assert.Equal("attachment; filename=\"Package@swift-6.swift\"", alternate.Content.Headers.ContentDisposition?.ToString());
        Assert.Equal(swift6, await alternate.Content.ReadAsStringAsync());

        using var missing = await GetAsync(manifestUrl + "?swift-version=5.9");
        Assert.Equal(HttpStatusCode.SeeOther, missing.StatusCode);
        Assert.Equal($"http://localhost/{manifestUrl}", missing.Headers.Location?.ToString());
    }

    [Fact]
    public async Task IdentifiersAreComparedRegardlessOfCase()
    {
        await _app.CreateSwiftFeedAsync(Feed);
        await PublishAsync("1.0.0");

        using var other = await _client.PublishAsync(Feed, "MONA.linkedlist", "1.0.0", SwiftTestClient.SourceArchive());
        var release = await _client.GetReleaseAsync(Feed, "MONA.linkedlist", "1.0.0");

        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Equal("mona.LinkedList", release["id"]!.GetValue<string>());
        Assert.Equal(new[] { "1.0.0" }, await _client.GetReleaseVersionsAsync(Feed, "Mona.LINKEDLIST"));
    }

    [Fact]
    public async Task ReleasesAreListedNewestFirst()
    {
        await _app.CreateSwiftFeedAsync(Feed);
        foreach (var version in new[] { "1.10.0", "1.0.0", "2.0.0-beta.1", "1.9.0" })
        {
            await PublishAsync(version);
        }

        Assert.Equal(new[] { "2.0.0-beta.1", "1.10.0", "1.9.0", "1.0.0" }, await _client.GetReleaseVersionsAsync(Feed, Package));
        Assert.True((await GetVersionAsync("2.0.0-beta.1")).IsPrerelease);
    }

    [Fact]
    public async Task UnlistedReleaseIsLeftOutOfTheListButStillServed()
    {
        await _app.CreateSwiftFeedAsync(Feed);
        await PublishAsync("1.0.0");
        await PublishAsync("1.1.0");
        await SetListedAsync("1.1.0", listed: false);

        using var zip = await GetAsync(SwiftTestClient.ReleaseUrl(Feed, Package, "1.1.0") + ".zip");

        Assert.Equal(new[] { "1.0.0" }, await _client.GetReleaseVersionsAsync(Feed, Package));
        Assert.Equal(HttpStatusCode.OK, zip.StatusCode);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+build.5")]
    public async Task ExistingVersionIsRefused(string version)
    {
        await _app.CreateSwiftFeedAsync(Feed);
        await PublishAsync("1.0.0");

        using var again = await _client.PublishAsync(Feed, Package, version, SwiftTestClient.SourceArchive(readme: "changed"));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("application/problem+json", again.Content.Headers.ContentType?.MediaType);
        Assert.Equal("""{"detail":"Version 1.0.0 of package mona.LinkedList already exists."}""", await again.Content.ReadAsStringAsync());
        Assert.False((await GetVersionAsync("1.0.0")).HasReadme);
    }

    [Fact]
    public async Task OverwritingFeedReplacesTheRelease()
    {
        await _app.CreateSwiftFeedAsync(Feed, configure: f => f.AllowPackageOverwrites = PackageOverwriteAllowed.True);
        await PublishAsync("1.0.0");

        var replacement = SwiftTestClient.SourceArchive(readme: "second");
        using var response = await _client.PublishAsync(Feed, Package, "1.0.0", replacement);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(replacement, await _client.GetByteArrayAsync(SwiftTestClient.ReleaseUrl(Feed, Package, "1.0.0") + ".zip"));
    }

    [Fact]
    public async Task PrereleaseOnlyFeedReplacesOnlyPrereleases()
    {
        await _app.CreateSwiftFeedAsync(Feed, configure: f => f.AllowPackageOverwrites = PackageOverwriteAllowed.PrereleaseOnly);
        await PublishAsync("1.0.0");
        await PublishAsync("2.0.0-beta.1");

        using var release = await _client.PublishAsync(Feed, Package, "1.0.0", SwiftTestClient.SourceArchive());
        using var prerelease = await _client.PublishAsync(Feed, Package, "2.0.0-beta.1", SwiftTestClient.SourceArchive());

        Assert.Equal(HttpStatusCode.Conflict, release.StatusCode);
        Assert.Equal(HttpStatusCode.Created, prerelease.StatusCode);
    }

    public static IEnumerable<object[]> InvalidPublishes()
    {
        var archive = SwiftTestClient.SourceArchive();
        yield return ["mo--na.LinkedList", "1.0.0", archive, null, HttpStatusCode.BadRequest, "mo--na is not a valid scope"];
        yield return ["mona.Linked__List", "1.0.0", archive, null, HttpStatusCode.BadRequest, "Linked__List is not a valid package name"];
        yield return [Package, "1.0", archive, null, HttpStatusCode.BadRequest, "1.0 is not a semantic version"];
        yield return [Package, "1.0.0", new byte[] { 1, 2, 3 }, null, HttpStatusCode.UnprocessableEntity, "not a valid zip file"];
        yield return [Package, "1.0.0", SwiftTestClient.SourceArchive(extraFiles: new Dictionary<string, string> { ["Other/Package.swift"] = "" }), null,
            HttpStatusCode.UnprocessableEntity, "one folder at its top level"];
        yield return [Package, "1.0.0", archive, """{"author": "Mona"}""", HttpStatusCode.UnprocessableEntity, "author must be an object with a name"];
    }

    [Theory]
    [MemberData(nameof(InvalidPublishes))]
    public async Task InvalidPublishIsRejectedWithAProblem(string identifier, string version, byte[] archive, string metadata, HttpStatusCode status, string detail)
    {
        await _app.CreateSwiftFeedAsync(Feed);

        using var response = await _client.PublishAsync(Feed, identifier, version, archive, metadata);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(detail, JsonNode.Parse(await response.Content.ReadAsStringAsync())!["detail"]!.GetValue<string>());
    }

    [Fact]
    public async Task PublishThatIsNotAMultipartBodyIsRejected()
    {
        await _app.CreateSwiftFeedAsync(Feed);
        var url = SwiftTestClient.ReleaseUrl(Feed, Package, "1.0.0");

        using var zip = await _client.PutAsync(url, new ByteArrayContent(SwiftTestClient.SourceArchive()));
        using var noArchive = await _client.PutAsync(url, new MultipartFormDataContent { { new StringContent("{}"), "metadata" } });

        Assert.Equal(HttpStatusCode.BadRequest, zip.StatusCode);
        Assert.Contains("multipart/form-data", await zip.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noArchive.StatusCode);
        Assert.Contains("no source archive", await noArchive.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SignatureIsStoredAndServedWithTheRelease()
    {
        await _app.CreateSwiftFeedAsync(Feed);
        var signature = RandomNumberGenerator.GetBytes(64);

        using var publish = await _client.PublishAsync(Feed, Package, "1.0.0", SwiftTestClient.SourceArchive(), signature: signature, signatureFormat: "cms-1.0.0");
        var release = await _client.GetReleaseAsync(Feed, Package, "1.0.0");
        using var zip = await GetAsync(SwiftTestClient.ReleaseUrl(Feed, Package, "1.0.0") + ".zip");

        Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        var signing = release["resources"]![0]!["signing"]!;
        Assert.Equal(Convert.ToBase64String(signature), signing["signatureBase64Encoded"]!.GetValue<string>());
        Assert.Equal("cms-1.0.0", signing["signatureFormat"]!.GetValue<string>());
        Assert.Equal("cms-1.0.0", zip.Headers.GetValues("X-Swift-Package-Signature-Format").Single());
        Assert.Equal(Convert.ToBase64String(signature), zip.Headers.GetValues("X-Swift-Package-Signature").Single());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pgp")]
    public async Task SignatureOfAnotherFormatIsRefused(string format)
    {
        await _app.CreateSwiftFeedAsync(Feed);

        using var response = await _client.PublishAsync(Feed, Package, "1.0.0", SwiftTestClient.SourceArchive(), signature: [1, 2, 3], signatureFormat: format);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("is not supported", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadOnlyFeedRefusesPublishing()
    {
        await _app.CreateSwiftFeedAsync(Feed, configure: f => f.IsReadOnlyMode = true);

        using var response = await _client.PublishAsync(Feed, Package, "1.0.0", SwiftTestClient.SourceArchive());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("read-only", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ArchiveLargerThanTheFeedLimitIsRejected()
    {
        await _app.CreateSwiftFeedAsync(Feed, configure: f => f.MaxPackageSizeMiB = 1);

        using var response = await _client.PublishAsync(Feed, Package, "1.0.0", RandomNumberGenerator.GetBytes(1024 * 1024 + 1));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("1 MiB", await response.Content.ReadAsStringAsync());
        Assert.Null(await _client.GetReleaseVersionsAsync(Feed, Package));
    }

    [Fact]
    public async Task ConfiguredApiKeyIsNeededToPublish()
    {
        using var app = new PaGettoApplication(_output, null, dict => dict["Authentication:ApiKeys:0:Key"] = "secret-key");
        using var client = app.CreateClient();
        await app.CreateSwiftFeedAsync(Feed);

        using var anonymous = await client.PublishAsync(Feed, Package, "1.0.0", SwiftTestClient.SourceArchive());

        client.UseBasicCredentials("ci", "wrong-key");
        using var wrong = await client.PublishAsync(Feed, Package, "1.0.0", SwiftTestClient.SourceArchive());

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "secret-key");
        using var right = await client.PublishAsync(Feed, Package, "1.0.0", SwiftTestClient.SourceArchive());

        // Swift Package Manager hangs on Linux on a 401 with a challenge, so there is none.
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Empty(anonymous.Headers.WwwAuthenticate);
        Assert.Contains("swift package-registry login", await anonymous.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Created, right.StatusCode);
    }

    [Fact]
    public async Task LoginAnswersWithTheRegistryOrTheLoginUrl()
    {
        await _app.CreateSwiftFeedAsync(Feed);

        using var registry = await _client.PostAsync($"feeds/{Feed}/swift", null);
        using var login = await _client.PostAsync($"feeds/{Feed}/swift/login", null);

        Assert.Equal(HttpStatusCode.OK, registry.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task IdentifiersAreLookedUpByRepositoryUrl()
    {
        await _app.CreateSwiftFeedAsync(Feed);
        await PublishAsync("1.0.0", metadata: SwiftTestClient.Metadata(repositoryUrls: "https://github.com/mona/LinkedList"));
        using (await _client.PublishAsync(Feed, "mona.Other", "1.0.0", SwiftTestClient.SourceArchive(), SwiftTestClient.Metadata()))
        {
        }

        var found = await _client.GetStringAsync($"feeds/{Feed}/swift/identifiers?url={Uri.EscapeDataString("git@github.com:mona/LinkedList.git")}");
        using var unknown = await GetAsync($"feeds/{Feed}/swift/identifiers?url=https://github.com/mona/Other");
        using var noUrl = await GetAsync($"feeds/{Feed}/swift/identifiers");

        Assert.Equal("""{"identifiers":["mona.LinkedList"]}""", found);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noUrl.StatusCode);
    }

    [Fact]
    public async Task OtherApiVersionIsRefused()
    {
        await _app.CreateSwiftFeedAsync(Feed);
        await PublishAsync("1.0.0");

        using var request = new HttpRequestMessage(HttpMethod.Get, SwiftTestClient.ReleaseUrl(Feed, Package));
        request.Headers.Accept.ParseAdd("application/vnd.swift.registry.v2+json");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Contains("version 1", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("swift/mona/Other")]
    [InlineData("swift/mona/LinkedList/9.9.9")]
    [InlineData("swift/mona/LinkedList/9.9.9.zip")]
    [InlineData("swift/mona/LinkedList/9.9.9/Package.swift")]
    [InlineData("swift/mona/LinkedList/not-a-version")]
    [InlineData("swift/mo--na/LinkedList")]
    [InlineData("swift/mona")]
    [InlineData("swift/mona/LinkedList/1.0.0/Package@swift-5.swift")]
    public async Task UnknownPackagesAndOtherPathsAreNotFound(string path)
    {
        await _app.CreateSwiftFeedAsync(Feed);
        await PublishAsync("1.0.0");

        using var response = await GetAsync($"feeds/{Feed}/{path}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "v3/index.json")]
    [InlineData("PUT", "api/v2/package")]
    [InlineData("GET", "cocoapods/CocoaPods-version.yml")]
    [InlineData("GET", "composer/packages.json")]
    [InlineData("GET", "upload")]
    public async Task OtherApisAreNotFoundOnASwiftFeed(string method, string path)
    {
        await _app.CreateSwiftFeedAsync(Feed);

        using var request = new HttpRequestMessage(new HttpMethod(method), $"feeds/{Feed}/{path}");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "swift/mona/LinkedList")]
    [InlineData("GET", "swift/mona/LinkedList/1.0.0")]
    [InlineData("GET", "swift/mona/LinkedList/1.0.0.zip")]
    [InlineData("GET", "swift/mona/LinkedList/1.0.0/Package.swift")]
    [InlineData("GET", "swift/identifiers?url=https://github.com/mona/LinkedList")]
    [InlineData("PUT", "swift/mona/LinkedList/1.0.0")]
    [InlineData("POST", "swift/login")]
    public async Task SwiftApiIsNotFoundOnOtherFeeds(string method, string path)
    {
        await _app.CreateFeedAsync("nuget");
        await _app.CreateCocoaPodsFeedAsync("pods");

        foreach (var url in new[] { $"feeds/nuget/{path}", $"feeds/pods/{path}", path })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task WebUiShowsTheFeedAndItsPackages()
    {
        await _app.CreateSwiftFeedAsync(Feed);
        await PublishAsync("1.0.0", SwiftTestClient.SourceArchive(readme: "# LinkedList\n\nThe readme."),
            SwiftTestClient.Metadata("<script>alert(1)</script>", "https://github.com/mona/LinkedList"));

        var index = await _client.GetStringAsync($"feeds/{Feed}/");
        Assert.Contains("Swift feed", index);
        Assert.Contains("<span class=\"bgt-badge ms-1\">Swift</span>", index);
        Assert.Contains(">mona.LinkedList<", index);
        Assert.Contains(".package(id: &quot;mona.LinkedList&quot;, from: &quot;1.0.0&quot;)", index);
        Assert.DoesNotContain("<script>alert(1)</script>", index);

        var details = await _client.GetStringAsync($"feeds/{Feed}/artifacts/mona.LinkedList");
        Assert.Contains("<h1>mona.LinkedList</h1>", details);
        Assert.Contains("Swift tools 5.9", details);
        Assert.Contains("The readme.", details);
        Assert.Contains("Mona", details);
        Assert.Contains("https://github.com/mona/LinkedList", details);
        Assert.Contains("https://example.com/LICENSE", details);
        Assert.Contains($"http://localhost/feeds/{Feed}/swift", details);
        Assert.Contains($"/feeds/{Feed}/swift/mona/LinkedList/1.0.0.zip", details);
        Assert.Contains("Download zip", details);

        var connect = await _client.GetStringAsync($"feeds/{Feed}/connect");
        Assert.Contains($"http://localhost/feeds/{Feed}/swift", connect);
        Assert.Contains("swift package-registry set", connect);
        Assert.Contains("swift package-registry publish", connect);
        Assert.Contains("--allow-insecure-http", connect);
    }

    private async Task PublishAsync(string version, byte[] archive = null, string metadata = null)
    {
        using var response = await _client.PublishAsync(Feed, Package, version, archive ?? SwiftTestClient.SourceArchive(), metadata);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd(SwiftTestClient.JsonAccept);
        return await _client.SendAsync(request);
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
