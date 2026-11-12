using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
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
/// The npm registry API of an npm feed, in Legacy mode without credentials (open access). The
/// requests are the ones npm sends; see also the check with the real client in the docs.
/// </summary>
public class NpmApiIntegrationTests : IDisposable
{
    private const string Feed = "npm";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public NpmApiIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task PublishedPackageIsServedWithTarballsOfThisFeed()
    {
        await _app.CreateNpmFeedAsync(Feed);

        var tarball = NpmTarballBuilder.Create("my-package", "1.0.0", readme: "# Mine");
        using (var publish = await _client.PutJsonAsync(
            NpmTestClient.PackageUrl(Feed, "my-package"),
            NpmTestClient.CreatePublishBody("my-package", "1.0.0", tarball)))
        {
            Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        }

        using (var second = await _client.PublishAsync(Feed, "my-package", "1.1.0"))
        {
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        }

        using var response = await _client.GetAsync($"feeds/{Feed}/npm/my-package");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var packument = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("my-package", packument["name"]?.GetValue<string>());
        Assert.Equal("1.1.0", packument["dist-tags"]?["latest"]?.GetValue<string>());
        Assert.Equal(new[] { "1.0.0", "1.1.0" }, packument["versions"]!.AsObject().Select(v => v.Key));
        Assert.NotNull(packument["time"]?["1.0.0"]);
        Assert.NotNull(packument["_rev"]);

        var version = packument["versions"]!["1.0.0"]!;
        Assert.Equal("my-package@1.0.0", version["_id"]?.GetValue<string>());
        Assert.Equal("A test package.", version["description"]?.GetValue<string>());
        Assert.Null(version["readme"]);
        Assert.Null(version["deprecated"]);

        var dist = version["dist"]!;
        Assert.Equal("http://localhost/feeds/npm/npm/my-package/-/my-package-1.0.0.tgz", dist["tarball"]?.GetValue<string>());
        Assert.Equal("sha512-" + Convert.ToBase64String(SHA512.HashData(tarball)), dist["integrity"]?.GetValue<string>());

        using var download = await _client.GetAsync(dist["tarball"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(tarball, await download.Content.ReadAsByteArrayAsync());

        var stored = await GetStoredVersionAsync("my-package", "1.0.0");
        Assert.Equal(1, stored.Downloads);
        Assert.True(stored.HasReadme);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(tarball)), stored.ContentHash);
    }

    [Fact]
    public async Task ScopedPackageAnswersToBothFormsOfItsName()
    {
        await _app.CreateNpmFeedAsync(Feed);

        using (var publish = await _client.PublishAsync(Feed, "@my-scope/my-package", "1.0.0"))
        {
            Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        }

        var escaped = await _client.GetJsonObjectAsync($"feeds/{Feed}/npm/@my-scope%2fmy-package");
        var plain = await _client.GetJsonObjectAsync($"feeds/{Feed}/npm/@my-scope/my-package");

        Assert.Equal("@my-scope/my-package", escaped["name"]?.GetValue<string>());
        Assert.Equal(escaped.ToJsonString(), plain.ToJsonString());

        var tarballUrl = escaped["versions"]!["1.0.0"]!["dist"]!["tarball"]!.GetValue<string>();
        Assert.Equal("http://localhost/feeds/npm/npm/@my-scope/my-package/-/my-package-1.0.0.tgz", tarballUrl);

        using var download = await _client.GetAsync(tarballUrl);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Fact]
    public async Task UnknownPackageIsNotFoundWithAnNpmError()
    {
        await _app.CreateNpmFeedAsync(Feed);

        using var package = await _client.GetAsync($"feeds/{Feed}/npm/unknown");
        using var tarball = await _client.GetAsync($"feeds/{Feed}/npm/unknown/-/unknown-1.0.0.tgz");
        using var otherFile = await _client.GetAsync($"feeds/{Feed}/npm/unknown/-/other-1.0.0.tgz");

        Assert.Equal(HttpStatusCode.NotFound, package.StatusCode);
        Assert.Contains("unknown", await package.ReadNpmErrorAsync());
        Assert.Equal(HttpStatusCode.NotFound, tarball.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherFile.StatusCode);
    }

    [Fact]
    public async Task PublishingTheSameVersionAgainIsAConflict()
    {
        await _app.CreateNpmFeedAsync(Feed);
        using (await _client.PublishAsync(Feed, "my-package", "1.0.0"))
        {
        }

        using var again = await _client.PublishAsync(Feed, "my-package", "1.0.0");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already exists", await again.ReadNpmErrorAsync());
    }

    [Fact]
    public async Task FeedThatAllowsOverwritesReplacesTheVersion()
    {
        await _app.CreateNpmFeedAsync(Feed, configure: f => f.AllowPackageOverwrites = PackageOverwriteAllowed.True);
        using (await _client.PublishAsync(Feed, "my-package", "1.0.0"))
        {
        }

        var replacement = NpmTarballBuilder.Create("my-package", "1.0.0", readme: "# Replaced");
        using var again = await _client.PutJsonAsync(
            NpmTestClient.PackageUrl(Feed, "my-package"),
            NpmTestClient.CreatePublishBody("my-package", "1.0.0", replacement));

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(replacement)), (await GetStoredVersionAsync("my-package", "1.0.0")).ContentHash);
    }

    [Fact]
    public async Task RejectsATarballThatDoesNotMatchItsManifest()
    {
        await _app.CreateNpmFeedAsync(Feed);

        var otherTarball = NpmTarballBuilder.Create("my-package", "1.0.0", readme: "Other");
        var body = NpmTestClient.CreatePublishBody("my-package", "1.0.0");
        body["_attachments"]!["my-package-1.0.0.tgz"]!["data"] = Convert.ToBase64String(otherTarball);

        using var response = await _client.PutJsonAsync(NpmTestClient.PackageUrl(Feed, "my-package"), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("integrity", await response.ReadNpmErrorAsync());
        Assert.Null(await GetStoredVersionAsync("my-package", "1.0.0"));
    }

    [Theory]
    [InlineData("other-package", "1.0.0", "my-package", "has other-package 1.0.0")]
    [InlineData("my-package", "2.0.0", "my-package", "has my-package 2.0.0")]
    [InlineData("My-Package", "1.0.0", "My-Package", "not a valid package name")]
    public async Task RejectsAPackageWhoseNameOrVersionDoesNotMatch(string tarballName, string tarballVersion, string name, string message)
    {
        await _app.CreateNpmFeedAsync(Feed);

        var body = NpmTestClient.CreatePublishBody(name, "1.0.0", NpmTarballBuilder.Create(tarballName, tarballVersion));
        using var response = await _client.PutJsonAsync(NpmTestClient.PackageUrl(Feed, name), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, await response.ReadNpmErrorAsync());
    }

    [Fact]
    public async Task ReadOnlyFeedRefusesChanges()
    {
        await _app.CreateNpmFeedAsync(Feed, configure: f => f.IsReadOnlyMode = true);

        using var publish = await _client.PublishAsync(Feed, "my-package", "1.0.0");

        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
        Assert.Contains("read-only", await publish.ReadNpmErrorAsync());
    }

    [Fact]
    public async Task TarballLargerThanTheFeedLimitIsRejected()
    {
        await _app.CreateNpmFeedAsync(Feed, configure: f => f.MaxPackageSizeMiB = 1);

        // Random bytes don't compress, so the tarball is larger than 1 MiB.
        var noise = new byte[1536 * 1024];
        new Random(42).NextBytes(noise);
        var tarball = NpmTarballBuilder.Create(
            NpmTarballBuilder.CreateManifest("my-package", "1.0.0", new JsonObject { ["noise"] = Convert.ToBase64String(noise) }));

        using var response = await _client.PutJsonAsync(
            NpmTestClient.PackageUrl(Feed, "my-package"),
            NpmTestClient.CreatePublishBody("my-package", "1.0.0", tarball));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("1 MiB", await response.ReadNpmErrorAsync());
    }

    [Fact]
    public async Task DistTagsCanBeListedAddedAndRemoved()
    {
        await _app.CreateNpmFeedAsync(Feed);
        using (await _client.PublishAsync(Feed, "@scope/pkg", "1.0.0"))
        using (await _client.PublishAsync(Feed, "@scope/pkg", "2.0.0-beta.1", tag: "beta"))
        {
        }

        const string tags = $"feeds/{Feed}/npm/-/package/@scope%2fpkg/dist-tags";

        var listed = await _client.GetJsonObjectAsync(tags);
        Assert.Equal("1.0.0", listed["latest"]?.GetValue<string>());
        Assert.Equal("2.0.0-beta.1", listed["beta"]?.GetValue<string>());

        using (var add = await _client.PutJsonAsync($"{tags}/stable", JsonValue.Create("1.0.0")))
        {
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        }

        using (var move = await _client.PutJsonAsync($"{tags}/latest", JsonValue.Create("2.0.0-beta.1")))
        {
            Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        }

        var packument = await _client.GetJsonObjectAsync(NpmTestClient.PackageUrl(Feed, "@scope/pkg"));
        Assert.Equal("1.0.0", packument["dist-tags"]?["stable"]?.GetValue<string>());
        Assert.Equal("2.0.0-beta.1", packument["dist-tags"]?["latest"]?.GetValue<string>());

        using (var remove = await _client.DeleteAsync($"{tags}/stable"))
        {
            Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        }

        using var removeLatest = await _client.DeleteAsync($"{tags}/latest");
        using var unknownVersion = await _client.PutJsonAsync($"{tags}/next", JsonValue.Create("9.9.9"));
        using var rangeAsTag = await _client.PutJsonAsync($"{tags}/1.x", JsonValue.Create("1.0.0"));

        Assert.Equal(HttpStatusCode.BadRequest, removeLatest.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownVersion.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rangeAsTag.StatusCode);
        Assert.Null((await _client.GetJsonObjectAsync(tags))["stable"]);
    }

    [Fact]
    public async Task DeprecatingAVersionUnlistsItWithItsMessage()
    {
        await _app.CreateNpmFeedAsync(Feed);
        using (await _client.PublishAsync(Feed, "my-package", "1.0.0"))
        using (await _client.PublishAsync(Feed, "my-package", "2.0.0"))
        {
        }

        // Like npm deprecate: read the document for writing, change it and send it back.
        var url = NpmTestClient.PackageUrl(Feed, "my-package");
        var document = await _client.GetJsonObjectAsync(url + "?write=true");
        document["versions"]!["1.0.0"]!["deprecated"] = "Use 2.0.0";

        using (var deprecate = await _client.PutJsonAsync(url, document))
        {
            Assert.Equal(HttpStatusCode.OK, deprecate.StatusCode);
        }

        Assert.False((await GetStoredVersionAsync("my-package", "1.0.0")).Listed);
        Assert.True((await GetStoredVersionAsync("my-package", "2.0.0")).Listed);
        Assert.Equal("Use 2.0.0", (await _client.GetJsonObjectAsync(url))["versions"]!["1.0.0"]!["deprecated"]?.GetValue<string>());

        // npm undeprecate sends an empty message.
        document["versions"]!["1.0.0"]!["deprecated"] = "";
        using (var undeprecate = await _client.PutJsonAsync(url, document))
        {
            Assert.Equal(HttpStatusCode.OK, undeprecate.StatusCode);
        }

        Assert.True((await GetStoredVersionAsync("my-package", "1.0.0")).Listed);
        Assert.Null((await _client.GetJsonObjectAsync(url))["versions"]!["1.0.0"]!["deprecated"]);
    }

    [Fact]
    public async Task UnpublishingOneVersionWorksTheWayNpmDoesIt()
    {
        await _app.CreateNpmFeedAsync(Feed);
        using (await _client.PublishAsync(Feed, "@scope/pkg", "1.0.0"))
        using (await _client.PublishAsync(Feed, "@scope/pkg", "2.0.0", tag: "next"))
        {
        }

        // 1. The document without the version, sent to its revision.
        var url = NpmTestClient.PackageUrl(Feed, "@scope/pkg");
        var document = await _client.GetJsonObjectAsync(url + "?write=true");
        var tarballUrl = new Uri(document["versions"]!["2.0.0"]!["dist"]!["tarball"]!.GetValue<string>());
        document["versions"]!.AsObject().Remove("2.0.0");

        using (var update = await _client.PutJsonAsync($"{url}/-rev/{document["_rev"]}", document))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }

        // 2. The tarball, at its path below the registry URL: the feed's path appears twice.
        using (var delete = await _client.DeleteAsync($"feeds/{Feed}/npm{tarballUrl.AbsolutePath}/-rev/1-abc"))
        {
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        }

        var packument = await _client.GetJsonObjectAsync(url);
        Assert.Equal(new[] { "1.0.0" }, packument["versions"]!.AsObject().Select(v => v.Key));
        Assert.Null(packument["dist-tags"]?["next"]);
        Assert.Null(await GetStoredVersionAsync("@scope/pkg", "2.0.0"));

        using var download = await _client.GetAsync(tarballUrl);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);

        // The removal is audited once, with the version, although it happened in the first step.
        using var scope = _app.Services.CreateScope();
        var deletes = await scope.ServiceProvider.GetRequiredService<IContext>().AuditEvents
            .Where(e => e.Event == "package_delete_succeeded")
            .ToListAsync();
        var audited = Assert.Single(deletes);
        Assert.Equal("@scope/pkg", audited.PackageId);
        Assert.Equal("2.0.0", audited.PackageVersion);
    }

    [Fact]
    public async Task UnpublishingAWholePackageRemovesIt()
    {
        await _app.CreateNpmFeedAsync(Feed);
        using (await _client.PublishAsync(Feed, "my-package", "1.0.0"))
        using (await _client.PublishAsync(Feed, "my-package", "2.0.0"))
        {
        }

        using var unpublish = await _client.DeleteAsync($"feeds/{Feed}/npm/my-package/-rev/2-abc");
        using var again = await _client.DeleteAsync($"feeds/{Feed}/npm/my-package/-rev/2-abc");
        using var packument = await _client.GetAsync($"feeds/{Feed}/npm/my-package");

        Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, packument.StatusCode);
    }

    [Fact]
    public async Task SearchFindsPackagesOfTheFeed()
    {
        await _app.CreateNpmFeedAsync(Feed);
        using (await _client.PublishAsync(Feed, "left-pad", "1.0.0"))
        using (await _client.PublishAsync(Feed, "right-pad", "1.0.0"))
        {
        }

        var result = await _client.GetJsonObjectAsync($"feeds/{Feed}/npm/-/v1/search?text=left&size=5");

        Assert.Equal(1, result["total"]?.GetValue<int>());
        var package = Assert.Single(result["objects"]!.AsArray())!["package"]!;
        Assert.Equal("left-pad", package["name"]?.GetValue<string>());
        Assert.Equal("1.0.0", package["version"]?.GetValue<string>());
        Assert.NotNull(package["maintainers"]);
    }

    [Fact]
    public async Task PingAndWhoAmIAnswer()
    {
        await _app.CreateNpmFeedAsync(Feed);

        using var ping = await _client.GetAsync($"feeds/{Feed}/npm/-/ping");
        var whoami = await _client.GetJsonObjectAsync($"feeds/{Feed}/npm/-/whoami");

        Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
        Assert.NotNull(whoami["username"]);
    }

    [Fact]
    public async Task EachEcosystemOnlyAnswersOnItsOwnFeeds()
    {
        await _app.CreateNpmFeedAsync(Feed);
        using (await _client.PublishAsync(Feed, "my-package", "1.0.0"))
        {
        }

        using var npmOnNuGet = await _client.GetAsync("feeds/default/npm/my-package");
        using var publishOnNuGet = await _client.PublishAsync("default", "my-package", "1.0.0");
        using var nugetOnNpm = await _client.GetAsync($"feeds/{Feed}/v3/index.json");
        using var pubOnNpm = await _client.GetAsync($"feeds/{Feed}/api/packages/my-package");

        Assert.Equal(HttpStatusCode.NotFound, npmOnNuGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, publishOnNuGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nugetOnNpm.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, pubOnNpm.StatusCode);
    }

    [Fact]
    public async Task InvalidJsonIsABadRequest()
    {
        await _app.CreateNpmFeedAsync(Feed);

        using var response = await _client.PutAsync(
            NpmTestClient.PackageUrl(Feed, "my-package"),
            new StringContent("{ nope", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<ArtifactVersion> GetStoredVersionAsync(string name, string version)
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return await context.ArtifactVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Artifact.Name == name && v.Version == version, CancellationToken.None);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
