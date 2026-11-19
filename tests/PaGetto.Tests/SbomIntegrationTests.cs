using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Npm;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The SBOM of a stored version: <c>GET {feed}/api/v1/sbom/{id}/{version}</c> and the links of the package pages.
/// </summary>
public class SbomIntegrationTests
{
    private const string Password = "LocalPassword123!";

    private readonly ITestOutputHelper _output;

    public SbomIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ServesTheSbomOfANuGetPackage()
    {
        using var app = new PaGettoApplication(_output);
        await app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/api/v1/sbom/TestData/1.2.3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.cyclonedx+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("TestData-1.2.3.cdx.json", response.Content.Headers.ContentDisposition?.FileName);

        var component = JsonNode.Parse(await response.Content.ReadAsStringAsync())["metadata"]["component"];
        Assert.Equal("TestData", (string)component["name"]);
        Assert.Equal("1.2.3", (string)component["version"]);
        Assert.Equal("pkg:nuget/TestData@1.2.3", (string)component["purl"]);

        // Routing lowercases the link; NuGet ids are case-insensitive.
        Assert.Contains("href=\"/api/v1/sbom/testdata/1.2.3\"", await client.GetStringAsync("/packages/TestData"));
        using var linked = await client.GetAsync("/api/v1/sbom/testdata/1.2.3");
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
    }

    [Fact]
    public async Task ServesTheSbomOfAScopedNpmPackage()
    {
        using var app = new PaGettoApplication(_output);
        var feed = await app.CreateNpmFeedAsync("js");
        await PublishNpmAsync(app, feed, "@acme/ui-kit", "1.0.0");
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/feeds/js/api/v1/sbom/%40acme%2Fui-kit/1.0.0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("acme_ui-kit-1.0.0.cdx.json", response.Content.Headers.ContentDisposition?.FileName);

        var component = JsonNode.Parse(await response.Content.ReadAsStringAsync())["metadata"]["component"];
        Assert.Equal("@acme", (string)component["group"]);
        Assert.Equal("ui-kit", (string)component["name"]);
        Assert.Equal("pkg:npm/%40acme/ui-kit@1.0.0", (string)component["purl"]);
        Assert.Equal("SHA-256", (string)component["hashes"][0]["alg"]);

        Assert.Contains("href=\"/feeds/js/api/v1/sbom/@acme%2Fui-kit/1.0.0\"", await client.GetStringAsync("/feeds/js/artifacts/%40acme%2Fui-kit"));
    }

    [Theory]
    [InlineData("/api/v1/sbom/TestData/9.9.9")]
    [InlineData("/api/v1/sbom/TestData/not-a-version")]
    [InlineData("/api/v1/sbom/Missing/1.2.3")]
    [InlineData("/feeds/js/api/v1/sbom/missing/1.0.0")]
    public async Task AnswersAVersionThatIsNotStoredWithNotFound(string path)
    {
        using var app = new PaGettoApplication(_output);
        await app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));
        await app.CreateNpmFeedAsync("js");
        using var client = app.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NeedsReadAccessToTheFeed()
    {
        using var app = new PaGettoApplication(_output, null, dict => dict["Authentication:Mode"] = "Local");
        await app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));

        using (var anonymous = app.CreateClient())
        using (var response = await anonymous.GetAsync("/api/v1/sbom/TestData/1.2.3"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var user = await WebUiSession.SeedLocalUserAsync(app, "reader", Password);
        using (var scope = app.Services.CreateScope())
        {
            var feed = await scope.ServiceProvider.GetRequiredService<IFeedService>().GetDefaultFeedAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IPermissionService>().GrantPermissionAsync(
                user.Id, PrincipalType.User, feed.Id, canPush: false, canPull: true, CancellationToken.None);
        }

        using var reader = await WebUiSession.SignInAsync(app, "reader", Password);
        var document = JsonNode.Parse(await reader.GetStringAsync("/api/v1/sbom/TestData/1.2.3"));

        Assert.Equal("TestData", (string)document["metadata"]["component"]["name"]);
    }

    private static async Task PublishNpmAsync(PaGettoApplication app, Feed feed, string name, string version)
    {
        using var scope = app.Services.CreateScope();
        var body = NpmTestClient.CreatePublishBody(name, version);

        using var document = JsonDocument.Parse(body.ToJsonString());
        var result = await scope.ServiceProvider.GetRequiredService<INpmPackageService>()
            .PublishAsync(feed, name, document.RootElement, maxTarballBytes: 1024 * 1024, CancellationToken.None);
        Assert.Equal(NpmStatus.Success, result.Status);
    }
}
