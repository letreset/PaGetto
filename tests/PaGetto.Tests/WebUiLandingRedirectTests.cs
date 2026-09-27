using System.Net;
using System.Threading.Tasks;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The index page redirects a signed-in user to the first feed they can pull. The redirect
/// must keep the configured PathBase, and must not keep the /feeds/{slug} segment of the
/// feed the user was redirected away from.
/// </summary>
public class WebUiLandingRedirectTests
{
    private const string Username = "reader";
    private const string Password = "Reader-Password-1!";

    private readonly ITestOutputHelper _output;

    public WebUiLandingRedirectTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/base")]
    public async Task FeedWithoutPullAccess_RedirectsToAccessibleFeedUnderPathBase(string pathBase)
    {
        using var app = new PaGettoApplication(_output, inMemoryConfiguration: dict =>
        {
            dict["Authentication:Mode"] = "Local";
            if (pathBase != null)
            {
                dict["PathBase"] = pathBase;
            }
        });
        await app.CreateFeedAsync("internal");
        await WebUiSession.SeedLocalUserAsync(app, Username, Password);
        using var session = await WebUiSession.SignInAsync(app, Username, Password);

        using var response = await session.Client.GetAsync($"{pathBase}/feeds/internal/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{pathBase}/feeds/default/", response.Headers.Location?.OriginalString);
    }
}
