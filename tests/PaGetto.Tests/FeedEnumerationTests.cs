using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// A caller who can't see a feed gets the same answer for it as for a slug that doesn't exist,
/// so feed slugs can't be enumerated.
/// </summary>
public class FeedEnumerationTests : IDisposable
{
    private const string Username = "enumuser";
    private const string Password = "EnumUserPass123!";

    private readonly PaGettoApplication _app;

    public FeedEnumerationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict => dict["Authentication:Mode"] = "Local");
    }

    [Theory]
    [InlineData("v3/search")]
    [InlineData("v3/registration/TestData/index.json")]
    [InlineData("v3/package/TestData/index.json")]
    public async Task AnonymousApiRequestIsChallengedForExistingAndMissingFeeds(string path)
    {
        await _app.CreateFeedAsync("secret");
        using var client = _app.CreateClient();

        using var existing = await client.GetAsync($"feeds/secret/{path}");
        using var missing = await client.GetAsync($"feeds/nope/{path}");

        Assert.Equal(HttpStatusCode.Unauthorized, existing.StatusCode);
        Assert.Equal(existing.StatusCode, missing.StatusCode);
        Assert.Equal(existing.Headers.WwwAuthenticate.ToString(), missing.Headers.WwwAuthenticate.ToString());
        Assert.Equal("Basic realm=\"NuGet Server\"", missing.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task AnonymousPushIsChallengedForExistingAndMissingFeeds()
    {
        await _app.CreateFeedAsync("secret");
        using var client = _app.CreateClient();

        using var existing = await client.PutAsync("feeds/secret/api/v2/package", new ByteArrayContent([]));
        using var missing = await client.PutAsync("feeds/nope/api/v2/package", new ByteArrayContent([]));

        Assert.Equal(HttpStatusCode.Unauthorized, existing.StatusCode);
        Assert.Equal(existing.StatusCode, missing.StatusCode);
    }

    [Fact]
    public async Task AnonymousServiceIndexLooksTheSameForExistingAndMissingFeeds()
    {
        await _app.CreateFeedAsync("secret");
        using var client = _app.CreateClient();

        using var existing = await client.GetAsync("feeds/secret/v3/index.json");
        using var missing = await client.GetAsync("feeds/nope/v3/index.json");

        Assert.Equal(HttpStatusCode.OK, existing.StatusCode);
        Assert.Equal(existing.StatusCode, missing.StatusCode);
        Assert.Equal(
            (await existing.Content.ReadAsStringAsync()).Replace("/feeds/secret/", "/feeds/nope/"),
            await missing.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("packages/Contoso.Logging")]
    [InlineData("Connect")]
    public async Task AnonymousUiRequestGetsTheSignInPromptForExistingAndMissingFeeds(string path)
    {
        await _app.CreateFeedAsync("secret", "Secret Feed");
        using var client = _app.CreateClient();

        using var existing = await client.GetAsync($"feeds/secret/{path}");
        using var missing = await client.GetAsync($"feeds/nope/{path}");

        Assert.Equal(HttpStatusCode.OK, existing.StatusCode);
        Assert.Equal(existing.StatusCode, missing.StatusCode);
        Assert.Contains("<title>Sign in required - PaGetto</title>", await missing.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("v3/search")]
    [InlineData("v3/registration/TestData/index.json")]
    public async Task SignedInApiRequestGets404ForForbiddenAndMissingFeeds(string path)
    {
        await _app.CreateFeedAsync("secret");
        await WebUiSession.SeedLocalUserAsync(_app, Username, Password);
        using var client = _app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}")));

        using var forbidden = await client.GetAsync($"feeds/secret/{path}");
        using var missing = await client.GetAsync($"feeds/nope/{path}");
        using var allowed = await client.GetAsync("v3/search");

        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.False(forbidden.Headers.Contains("WWW-Authenticate"));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v3/search")]
    public async Task SignedInBrowserGets404ForForbiddenAndMissingFeeds(string path)
    {
        await _app.CreateFeedAsync("secret");
        await WebUiSession.SeedLocalUserAsync(_app, Username, Password);
        using var session = await WebUiSession.SignInAsync(_app, Username, Password);

        using var forbidden = await session.Client.GetAsync($"feeds/secret/{path}");
        using var missing = await session.Client.GetAsync($"feeds/nope/{path}");

        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Null(forbidden.Headers.Location);
        Assert.False(forbidden.Headers.Contains("WWW-Authenticate"));
    }

    public void Dispose()
    {
        _app.Dispose();
    }
}
