using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Tests.Support;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The pub API of a Dart feed, in Legacy mode without credentials (open access).
/// </summary>
public class PubApiIntegrationTests : IDisposable
{
    private const string Feed = "dart";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public PubApiIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task PublishedPackageIsListedAndDownloadable()
    {
        await _app.CreatePubFeedAsync(Feed);

        var first = PubArchiveBuilder.Create("my_package", "1.0.0+1", readme: "# First").ToArray();
        var second = PubArchiveBuilder.Create("my_package", "1.0.0+2", readme: "# Second").ToArray();

        foreach (var archive in new[] { first, second })
        {
            using var finalize = await _client.PublishAsync(Feed, new System.IO.MemoryStream(archive));

            Assert.Equal(HttpStatusCode.OK, finalize.StatusCode);
            Assert.Equal(PubTestClient.PubMediaType, finalize.Content.Headers.ContentType?.MediaType);
            Assert.Contains("my_package", (await finalize.ReadJsonAsync()).GetProperty("success").GetProperty("message").GetString());
        }

        using var response = await _client.GetAsync($"feeds/{Feed}/api/packages/my_package");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PubTestClient.PubMediaType, response.Content.Headers.ContentType?.MediaType);

        // Build metadata makes these two different versions, unlike in NuGet.
        var package = await response.ReadJsonAsync();
        Assert.Equal("my_package", package.GetProperty("name").GetString());
        Assert.Equal("1.0.0+2", package.GetProperty("latest").GetProperty("version").GetString());

        var versions = package.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(new[] { "1.0.0+1", "1.0.0+2" }, versions.Select(v => v.GetProperty("version").GetString()));

        var listed = versions[0];
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(first)), listed.GetProperty("archive_sha256").GetString());
        Assert.Equal("my_package", listed.GetProperty("pubspec").GetProperty("name").GetString());
        Assert.Equal("1.0.0+1", listed.GetProperty("pubspec").GetProperty("version").GetString());
        Assert.False(listed.TryGetProperty("retracted", out _));

        // The archive is served by this feed, at the URL the listing gives.
        var archiveUrl = listed.GetProperty("archive_url").GetString();
        Assert.StartsWith($"http://localhost/feeds/{Feed}/packages/my_package/versions/", archiveUrl);

        using var download = await _client.GetAsync(archiveUrl);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(first, await download.Content.ReadAsByteArrayAsync());

        Assert.Equal(1, await GetDownloadsAsync("my_package", "1.0.0+1"));
        Assert.Equal(0, await GetDownloadsAsync("my_package", "1.0.0+2"));
    }

    [Fact]
    public async Task SingleVersionEndpointOfOlderClientsWorks()
    {
        await _app.CreatePubFeedAsync(Feed);
        using (await _client.PublishAsync(Feed, PubArchiveBuilder.Create("my_package", "1.2.3")))
        {
        }

        using var found = await _client.GetAsync($"feeds/{Feed}/api/packages/my_package/versions/1.2.3");
        using var missing = await _client.GetAsync($"feeds/{Feed}/api/packages/my_package/versions/9.9.9");

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal("1.2.3", (await found.ReadJsonAsync()).GetProperty("version").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task UnknownPackageIsNotFoundWithAPubError()
    {
        await _app.CreatePubFeedAsync(Feed);

        using var package = await _client.GetAsync($"feeds/{Feed}/api/packages/unknown");
        using var archive = await _client.GetAsync($"feeds/{Feed}/packages/unknown/versions/1.0.0.tar.gz");

        Assert.Equal(HttpStatusCode.NotFound, package.StatusCode);
        Assert.Contains("unknown", await package.ReadErrorMessageAsync());
        Assert.Equal(HttpStatusCode.NotFound, archive.StatusCode);
    }

    [Fact]
    public async Task PublishingTheSameVersionAgainIsRejectedWithAMessage()
    {
        await _app.CreatePubFeedAsync(Feed);
        using (await _client.PublishAsync(Feed, PubArchiveBuilder.Create("my_package", "1.0.0")))
        {
        }

        using var finalize = await _client.PublishAsync(Feed, PubArchiveBuilder.Create("my_package", "1.0.0", readme: "changed"));

        Assert.Equal(HttpStatusCode.BadRequest, finalize.StatusCode);
        Assert.Contains("already exists", await finalize.ReadErrorMessageAsync());
    }

    [Fact]
    public async Task PublishingAnInvalidArchiveIsRejectedWithAMessage()
    {
        await _app.CreatePubFeedAsync(Feed);

        using var finalize = await _client.PublishAsync(Feed, new System.IO.MemoryStream("not an archive"u8.ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, finalize.StatusCode);
        Assert.Contains("archive", await finalize.ReadErrorMessageAsync());
    }

    [Fact]
    public async Task FinalizingWithoutAnUploadDoesNotClaimSuccess()
    {
        await _app.CreatePubFeedAsync(Feed);

        using var finalize = await _client.GetAsync($"feeds/{Feed}/api/packages/versions/newUploadFinish?package=my_package&version=1.0.0");

        Assert.Equal(HttpStatusCode.BadRequest, finalize.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyFeedRefusesToPublish()
    {
        var feed = await _app.CreatePubFeedAsync(Feed);
        await UpdateFeedAsync(feed.Id, f => f.IsReadOnlyMode = true);

        using var response = await _client.GetAsync($"feeds/{Feed}/api/packages/versions/new");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("read-only", await response.ReadErrorMessageAsync());
    }

    [Fact]
    public async Task PackageLargerThanTheFeedLimitIsRejected()
    {
        var feed = await _app.CreatePubFeedAsync(Feed);
        await UpdateFeedAsync(feed.Id, f => f.MaxPackageSizeMiB = 1);

        // Random text doesn't compress, so the archive stays above 1 MiB.
        var readme = Convert.ToBase64String(RandomNumberGenerator.GetBytes(1024 * 1024));
        using var finalize = await _client.PublishAsync(Feed, PubArchiveBuilder.Create("my_package", "1.0.0", readme));

        Assert.Equal(HttpStatusCode.BadRequest, finalize.StatusCode);
        Assert.Contains("1 MiB", await finalize.ReadErrorMessageAsync());
    }

    [Theory]
    [InlineData("GET", "v3/index.json")]
    [InlineData("GET", "v3/search")]
    [InlineData("GET", "v3/package/my_package/index.json")]
    [InlineData("PUT", "api/v2/package")]
    [InlineData("PUT", "api/v2/symbol")]
    [InlineData("GET", "packages/my_package")]
    [InlineData("GET", "upload")]
    public async Task NuGetApiAndPagesAreNotFoundOnADartFeed(string method, string path)
    {
        await _app.CreatePubFeedAsync(Feed);

        using var request = new HttpRequestMessage(new HttpMethod(method), $"feeds/{Feed}/{path}");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("api/packages/my_package")]
    [InlineData("api/packages/versions/new")]
    [InlineData("packages/my_package/versions/1.0.0.tar.gz")]
    [InlineData("artifacts/my_package")]
    public async Task PubApiAndPagesAreNotFoundOnANuGetFeed(string path)
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
        await _app.CreatePubFeedAsync(Feed);
        using (await _client.PublishAsync(Feed, PubArchiveBuilder.Create(
            "my_package", "1.0.0+1", readme: "# Hello from the readme\n\n<script>alert(1)</script>", extraPubspec: "homepage: https://example.com\ndependencies:\n  http: ^1.0.0\n")))
        {
        }

        var index = await _client.GetStringAsync($"feeds/{Feed}/");
        Assert.Contains("my_package", index);
        Assert.Contains("A test package.", index);
        Assert.Contains("1 package", index);

        var search = await _client.GetStringAsync($"feeds/{Feed}/?q=nothing-like-this");
        Assert.DoesNotContain("A test package.", search);

        var details = await _client.GetStringAsync($"feeds/{Feed}/artifacts/my_package");
        Assert.Contains("Hello from the readme", details);
        Assert.Contains("https://example.com/", details);
        Assert.Contains("http://localhost/feeds/dart", details);

        // Raw HTML of a readme is shown as text.
        Assert.DoesNotContain("<script>alert(1)</script>", details);

        using var unknownVersion = await _client.GetAsync($"feeds/{Feed}/artifacts/my_package/9.9.9");
        Assert.Contains("Version not found", await unknownVersion.Content.ReadAsStringAsync());

        var connect = await _client.GetStringAsync($"feeds/{Feed}/connect");
        Assert.Contains("dart pub token add http://localhost/feeds/dart", connect);

        var statistics = await _client.GetStringAsync($"feeds/{Feed}/stats");
        Assert.Contains("/feeds/dart/artifacts/my_package", statistics);
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

    private async Task UpdateFeedAsync(Guid feedId, Action<Feed> update)
    {
        using var scope = _app.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = await feedService.GetFeedByIdAsync(feedId, CancellationToken.None);
        update(feed);
        await feedService.UpdateFeedAsync(feed, CancellationToken.None);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
