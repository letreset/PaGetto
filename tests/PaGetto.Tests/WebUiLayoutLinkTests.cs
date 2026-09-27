using System.Net;
using System.Threading.Tasks;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The layout's feed-independent links (brand, feed switcher, account and admin pages) are
/// built from the application's base path, never from the current feed's path.
/// </summary>
public class WebUiLayoutLinkTests
{
    private const string Username = "admin";
    private const string Password = "Admin-Password-1!";

    // A configured PathBase that itself contains "/feeds/".
    private const string PathBase = "/apps/feeds/pagetto";

    private readonly ITestOutputHelper _output;

    public WebUiLayoutLinkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/feeds/default/")]
    public async Task Links_KeepTheConfiguredPathBase(string path)
    {
        using var app = new PaGettoApplication(_output, inMemoryConfiguration: dict =>
        {
            dict["Authentication:Mode"] = "Local";
            dict["PathBase"] = PathBase;
        });
        // A second feed makes the layout render the feed switcher.
        await app.CreateFeedAsync("internal");
        await WebUiSession.SeedLocalUserAsync(app, Username, Password, isAdmin: true);
        using var session = await WebUiSession.SignInAsync(app, Username, Password);

        using var response = await session.Client.GetAsync($"{PathBase}{path}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"href=\"{PathBase}/\"", body);
        Assert.Contains($"href=\"{PathBase}/feeds/internal\"", body);
        Assert.Contains($"href=\"{PathBase}/Account/Tokens\"", body);
        Assert.Contains($"href=\"{PathBase}/Admin/Feeds\"", body);
    }
}
