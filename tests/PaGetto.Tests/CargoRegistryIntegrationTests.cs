using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
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
/// The registry of a Cargo feed, in Legacy mode without credentials (open access).
/// </summary>
public class CargoRegistryIntegrationTests : IDisposable
{
    private const string Feed = "rust";

    private readonly ITestOutputHelper _output;
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public CargoRegistryIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task ConfigPointsCargoAtTheFeed()
    {
        await _app.CreateCargoFeedAsync(Feed);

        using var response = await _client.GetAsync(CargoTestClient.IndexUrl(Feed) + "config.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var config = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal($"http://localhost/feeds/{Feed}/cargo/api/v1/crates", config.RootElement.GetProperty("dl").GetString());
        Assert.Equal($"http://localhost/feeds/{Feed}/cargo", config.RootElement.GetProperty("api").GetString());

        // Open access: Cargo needs no token to read.
        Assert.False(config.RootElement.GetProperty("auth-required").GetBoolean());
    }

    [Fact]
    public async Task PublishedCrateIsInTheIndexAndDownloadable()
    {
        await _app.CreateCargoFeedAsync(Feed);

        var crate = CargoCrateBuilder.Create("My_Crate", "1.0.0", readme: "# Hello");
        var metadata = CargoCrateBuilder.Metadata("My_Crate", "1.0.0");
        metadata["deps"] = new JsonArray(new JsonObject { ["name"] = "serde", ["version_req"] = "^1.0", ["kind"] = "normal" });

        using var publish = await _client.PublishCrateAsync(Feed, metadata, crate);

        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        using (var result = JsonDocument.Parse(await publish.Content.ReadAsStringAsync()))
        {
            Assert.Equal(JsonValueKind.Object, result.RootElement.GetProperty("warnings").ValueKind);
        }

        using (await _client.PublishCrateAsync(Feed, "My_Crate", "1.1.0-beta.1"))
        {
        }

        var lines = await _client.GetIndexLinesAsync(Feed, "my_crate");
        Assert.Equal(new[] { "1.0.0", "1.1.0-beta.1" }, lines.Select(l => l.GetProperty("vers").GetString()));

        var line = lines[0];
        Assert.Equal("My_Crate", line.GetProperty("name").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(crate)), line.GetProperty("cksum").GetString());
        Assert.False(line.GetProperty("yanked").GetBoolean());
        Assert.Equal("serde", line.GetProperty("deps")[0].GetProperty("name").GetString());
        Assert.Equal("^1.0", line.GetProperty("deps")[0].GetProperty("req").GetString());
        Assert.False(line.TryGetProperty("description", out _));

        // Cargo downloads from dl with the name as the index has it.
        using var download = await _client.GetAsync(CargoTestClient.DownloadUrl(Feed, "My_Crate", "1.0.0"));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(crate, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, await GetDownloadsAsync("my_crate", "1.0.0"));
    }

    [Theory]
    [InlineData("my/cr/my_crate")]
    [InlineData("my/_c/My_Crate")]
    [InlineData("3/m/my_crate")]
    [InlineData("my_crate")]
    public async Task IndexFileIsOnlyAtItsOwnPath(string path)
    {
        await _app.CreateCargoFeedAsync(Feed);
        using (await _client.PublishCrateAsync(Feed, "my_crate", "1.0.0"))
        {
        }

        using var response = await _client.GetAsync(CargoTestClient.IndexUrl(Feed) + path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownCrateIsNotFound()
    {
        await _app.CreateCargoFeedAsync(Feed);

        using var index = await _client.GetAsync(CargoTestClient.IndexFileUrl(Feed, "unknown"));
        using var download = await _client.GetAsync(CargoTestClient.DownloadUrl(Feed, "unknown", "1.0.0"));

        Assert.Equal(HttpStatusCode.NotFound, index.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Contains("unknown", await download.ReadCargoErrorAsync());
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+build.2")]
    public async Task PublishingAnExistingVersionIsRejected(string version)
    {
        await _app.CreateCargoFeedAsync(Feed);
        using (await _client.PublishCrateAsync(Feed, "my_crate", "1.0.0"))
        {
        }

        using var response = await _client.PublishCrateAsync(Feed, "my_crate", version);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("already exists", await response.ReadCargoErrorAsync());
    }

    [Fact]
    public async Task OverwritingFeedReplacesTheVersion()
    {
        await _app.CreateCargoFeedAsync(Feed, configure: f => f.AllowPackageOverwrites = PackageOverwriteAllowed.True);
        using (await _client.PublishCrateAsync(Feed, "my_crate", "1.0.0"))
        {
        }

        var replacement = CargoCrateBuilder.Create("my_crate", "1.0.0+build.2", readme: "changed");
        using var response = await _client.PublishCrateAsync(Feed, CargoCrateBuilder.Metadata("my_crate", "1.0.0+build.2"), replacement);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var line = Assert.Single(await _client.GetIndexLinesAsync(Feed, "my_crate"));
        Assert.Equal("1.0.0+build.2", line.GetProperty("vers").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(replacement)), line.GetProperty("cksum").GetString());
    }

    [Fact]
    public async Task CrateKeepsTheNameItWasFirstPublishedUnder()
    {
        await _app.CreateCargoFeedAsync(Feed);
        using (await _client.PublishCrateAsync(Feed, "My_Crate", "1.0.0"))
        {
        }

        using var response = await _client.PublishCrateAsync(Feed, "my_crate", "2.0.0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("named My_Crate", await response.ReadCargoErrorAsync());
    }

    [Theory]
    [InlineData("1crate", "1.0.0", "crate name")]
    [InlineData("my_crate", "1.0", "semantic version")]
    public async Task InvalidMetadataIsRejectedWithAMessage(string name, string version, string message)
    {
        await _app.CreateCargoFeedAsync(Feed);

        using var response = await _client.PublishCrateAsync(Feed, CargoCrateBuilder.Metadata(name, version), CargoCrateBuilder.Create("x", "1.0.0"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, await response.ReadCargoErrorAsync());
    }

    [Fact]
    public async Task CrateOfAnotherVersionIsRejected()
    {
        await _app.CreateCargoFeedAsync(Feed);

        using var response = await _client.PublishCrateAsync(Feed, CargoCrateBuilder.Metadata("my_crate", "2.0.0"), CargoCrateBuilder.Create("my_crate", "1.0.0"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("my_crate-2.0.0/Cargo.toml", await response.ReadCargoErrorAsync());
    }

    [Fact]
    public async Task YankingTakesTheVersionOutOfResolutionButNotOutOfTheFeed()
    {
        await _app.CreateCargoFeedAsync(Feed);
        using (await _client.PublishCrateAsync(Feed, "my_crate", "1.0.0"))
        {
        }

        using var yank = await _client.DeleteAsync($"feeds/{Feed}/cargo/api/v1/crates/my_crate/1.0.0/yank");

        Assert.Equal(HttpStatusCode.OK, yank.StatusCode);
        Assert.Equal("{\"ok\":true}", await yank.Content.ReadAsStringAsync());
        Assert.True(Assert.Single(await _client.GetIndexLinesAsync(Feed, "my_crate")).GetProperty("yanked").GetBoolean());

        using (var download = await _client.GetAsync(CargoTestClient.DownloadUrl(Feed, "my_crate", "1.0.0")))
        {
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        }

        using var unyank = await _client.PutAsync($"feeds/{Feed}/cargo/api/v1/crates/my_crate/1.0.0/unyank", null);

        Assert.Equal(HttpStatusCode.OK, unyank.StatusCode);
        Assert.False(Assert.Single(await _client.GetIndexLinesAsync(Feed, "my_crate")).GetProperty("yanked").GetBoolean());

        using var unknown = await _client.DeleteAsync($"feeds/{Feed}/cargo/api/v1/crates/my_crate/9.9.9/yank");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyFeedRefusesToPublishAndYank()
    {
        await _app.CreateCargoFeedAsync(Feed, configure: f => f.IsReadOnlyMode = true);

        using var publish = await _client.PublishCrateAsync(Feed, "my_crate", "1.0.0");
        using var yank = await _client.DeleteAsync($"feeds/{Feed}/cargo/api/v1/crates/my_crate/1.0.0/yank");

        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
        Assert.Contains("read-only", await publish.ReadCargoErrorAsync());
        Assert.Equal(HttpStatusCode.Forbidden, yank.StatusCode);
    }

    [Fact]
    public async Task CrateLargerThanTheFeedLimitIsRejected()
    {
        await _app.CreateCargoFeedAsync(Feed, configure: f => f.MaxPackageSizeMiB = 1);

        // Random text doesn't compress, so the crate stays above 1 MiB.
        var readme = Convert.ToBase64String(RandomNumberGenerator.GetBytes(1024 * 1024));
        using var response = await _client.PublishCrateAsync(
            Feed, CargoCrateBuilder.Metadata("my_crate", "1.0.0"), CargoCrateBuilder.Create("my_crate", "1.0.0", readme));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("1 MiB", await response.ReadCargoErrorAsync());
    }

    [Fact]
    public async Task SearchFindsCratesOfTheFeed()
    {
        await _app.CreateCargoFeedAsync(Feed);
        using (await _client.PublishCrateAsync(Feed, "My_Crate", "1.0.0"))
        {
        }

        using (await _client.PublishCrateAsync(Feed, "My_Crate", "1.2.0"))
        {
        }

        using var response = await _client.GetAsync($"feeds/{Feed}/cargo/api/v1/crates?q=crate&per_page=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var crate = Assert.Single(result.RootElement.GetProperty("crates").EnumerateArray());
        Assert.Equal("My_Crate", crate.GetProperty("name").GetString());
        Assert.Equal("1.2.0", crate.GetProperty("max_version").GetString());
        Assert.Equal("A test crate.", crate.GetProperty("description").GetString());
        Assert.Equal(1, result.RootElement.GetProperty("meta").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ConfiguredApiKeyIsNeededToPublish()
    {
        using var app = new PaGettoApplication(_output, null, dict => dict["Authentication:ApiKeys:0:Key"] = "secret-key");
        using var client = app.CreateClient();
        await app.CreateCargoFeedAsync(Feed);

        using var anonymous = await client.PublishCrateAsync(Feed, "my_crate", "1.0.0");

        client.UseCargoToken("wrong-key");
        using var wrong = await client.PublishCrateAsync(Feed, "my_crate", "1.0.0");

        client.UseCargoToken("secret-key");
        using var right = await client.PublishCrateAsync(Feed, "my_crate", "1.0.0");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
    }

    [Theory]
    [InlineData("GET", "v3/index.json")]
    [InlineData("PUT", "api/v2/package")]
    [InlineData("GET", "api/packages/my_crate")]
    [InlineData("GET", "npm/my_crate")]
    [InlineData("GET", "pypi/simple/")]
    [InlineData("GET", "upload")]
    public async Task OtherApisAreNotFoundOnACargoFeed(string method, string path)
    {
        await _app.CreateCargoFeedAsync(Feed);

        using var request = new HttpRequestMessage(new HttpMethod(method), $"feeds/{Feed}/{path}");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("cargo/index/config.json")]
    [InlineData("cargo/index/my/_c/my_crate")]
    [InlineData("cargo/api/v1/crates/my_crate/1.0.0/download")]
    public async Task CargoApiIsNotFoundOnANuGetFeed(string path)
    {
        await _app.CreateFeedAsync("nuget");

        using var named = await _client.GetAsync($"feeds/nuget/{path}");
        using var defaultFeed = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, named.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, defaultFeed.StatusCode);
    }

    [Fact]
    public async Task WebUiShowsTheFeedAndItsCrates()
    {
        await _app.CreateCargoFeedAsync(Feed);

        var metadata = CargoCrateBuilder.Metadata("My_Crate", "1.0.0");
        metadata["readme"] = "# Hello from the readme\n\n<script>alert(1)</script>";
        metadata["repository"] = "https://example.com/repo";
        metadata["keywords"] = new JsonArray("parser");
        metadata["rust_version"] = "1.70";
        metadata["deps"] = new JsonArray(new JsonObject { ["name"] = "serde", ["version_req"] = "^1.0", ["kind"] = "normal" });
        using (await _client.PublishCrateAsync(Feed, metadata))
        {
        }

        var index = await _client.GetStringAsync($"feeds/{Feed}/");
        Assert.Contains("Rust feed", index);
        Assert.Contains("<span class=\"bgt-badge ms-1\">Rust</span>", index);
        Assert.Contains("My_Crate", index);
        Assert.Contains("A test crate.", index);
        Assert.Contains($"cargo add My_Crate@1.0.0 --registry {Feed}", index);

        // The page is found by the lowercased name too.
        var details = await _client.GetStringAsync($"feeds/{Feed}/artifacts/my_crate");
        Assert.Contains("<h1>My_Crate</h1>", details);
        Assert.Contains("Hello from the readme", details);
        Assert.DoesNotContain("<script>alert(1)</script>", details);
        Assert.Contains("https://example.com/repo", details);
        Assert.Contains("Rust 1.70", details);
        Assert.Contains("parser", details);
        Assert.Contains("serde", details);
        Assert.Contains("MIT", details);
        Assert.Contains($"/feeds/{Feed}/cargo/api/v1/crates/My_Crate/1.0.0/download", details);

        var connect = await _client.GetStringAsync($"feeds/{Feed}/connect");
        Assert.Contains($"sparse&#x2B;http://localhost/feeds/{Feed}/cargo/index/", connect);
        Assert.Contains("CARGO_REGISTRIES_RUST_TOKEN", connect);

        // Cargo refuses an auth-required registry without a credential provider.
        Assert.Contains("credential-provider = [", connect);
        Assert.Contains("cargo:token", connect);
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
