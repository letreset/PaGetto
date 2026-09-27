using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Xml.Linq;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

public class PackageAtomFeedTests
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    private const string Password = "LocalPassword123!";

    private readonly ITestOutputHelper _output;

    public PackageAtomFeedTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ListsTheListedVersions()
    {
        using var app = new PaGettoApplication(_output);
        await app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/packages/TestData/atom.xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/atom+xml", response.Content.Headers.ContentType?.MediaType);

        var feed = XDocument.Parse(await response.Content.ReadAsStringAsync()).Root;
        Assert.Equal(Atom + "feed", feed.Name);
        Assert.Equal("http://localhost/packages/testdata/atom.xml", feed.Element(Atom + "id")?.Value);
        Assert.NotNull(feed.Element(Atom + "updated"));
        Assert.NotNull(feed.Element(Atom + "author")?.Element(Atom + "name"));

        var entry = Assert.Single(feed.Elements(Atom + "entry"));
        Assert.Equal("TestData 1.2.3", entry.Element(Atom + "title")?.Value);
        Assert.Equal("http://localhost/packages/testdata/1.2.3", entry.Element(Atom + "link")?.Attribute("href")?.Value);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", entry.Element(Atom + "updated")?.Value);
    }

    [Fact]
    public async Task WorksUnderAFeedPath()
    {
        using var app = new PaGettoApplication(_output);
        await app.CreateFeedAsync("internal");
        await app.AddPackageToFeedAsync(TestResources.GetResourceStream(TestResources.Package), "internal");
        using var client = app.CreateClient();

        var feed = XDocument.Parse(await client.GetStringAsync("/feeds/internal/packages/TestData/atom.xml")).Root;

        Assert.Equal("http://localhost/feeds/internal/packages/testdata/1.2.3",
            feed.Element(Atom + "entry")?.Element(Atom + "link")?.Attribute("href")?.Value);
    }

    [Fact]
    public async Task ReturnsNotFoundForAnUnknownPackage()
    {
        using var app = new PaGettoApplication(_output);
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/packages/Unknown/atom.xml");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LeavesOutUnlistedVersions()
    {
        using var app = new PaGettoApplication(_output);
        await app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));
        using var client = app.CreateClient();
        using (var unlist = await client.DeleteAsync("/api/v2/package/TestData/1.2.3"))
        {
            Assert.Equal(HttpStatusCode.NoContent, unlist.StatusCode);
        }

        using var response = await client.GetAsync("/packages/TestData/atom.xml");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RequiresBasicAuthAndPullPermissionInLocalMode()
    {
        using var app = new PaGettoApplication(_output, null, dict => dict["Authentication:Mode"] = "Local");
        await app.CreateFeedAsync("internal");
        await app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));
        await app.AddPackageToFeedAsync(TestResources.GetResourceStream(TestResources.Package), "internal");
        // Pull on the default feed only.
        await WebUiSession.SeedLocalUserAsync(app, "reader", Password);
        using var client = app.CreateClient();

        using (var anonymous = await client.GetAsync("/packages/TestData/atom.xml"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, await GetAsReaderAsync(client, "/packages/TestData/atom.xml"));

        // Without pull permission: the same 404 as on the package page.
        Assert.Equal(HttpStatusCode.NotFound, await GetAsReaderAsync(client, "/feeds/internal/packages/TestData/atom.xml"));
    }

    private static async Task<HttpStatusCode> GetAsReaderAsync(System.Net.Http.HttpClient client, string url)
    {
        using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
        request.Headers.Authorization = Basic("reader", Password);
        using var response = await client.SendAsync(request);

        return response.StatusCode;
    }

    private static System.Net.Http.Headers.AuthenticationHeaderValue Basic(string username, string password)
    {
        return new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}")));
    }
}
