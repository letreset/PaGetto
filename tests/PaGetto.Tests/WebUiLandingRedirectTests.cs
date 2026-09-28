using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The root index page redirects a signed-in user to the first feed they can pull, keeping the
/// configured PathBase. A /feeds/{slug} page the user can't pull returns 404 instead, the same
/// as a slug that doesn't exist.
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
    public async Task RootRedirectsToFirstAccessibleFeedUnderPathBase(string pathBase)
    {
        using var app = CreateApp(pathBase);
        var internalFeed = await app.CreateFeedAsync("internal");
        var user = await WebUiSession.SeedLocalUserAsync(app, Username, Password);
        using (var scope = app.Services.CreateScope())
        {
            var feeds = scope.ServiceProvider.GetRequiredService<IFeedService>();
            await feeds.ReorderFeedsAsync([internalFeed.Id, Feed.DefaultId], CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IPermissionService>().GrantPermissionAsync(
                user.Id, PrincipalType.User, internalFeed.Id, canPush: false, canPull: true, CancellationToken.None);
        }
        using var session = await WebUiSession.SignInAsync(app, Username, Password);

        using var response = await session.Client.GetAsync($"{pathBase}/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{pathBase}/feeds/internal/", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/base")]
    public async Task FeedWithoutPullAccessReturns404LikeMissingFeed(string pathBase)
    {
        using var app = CreateApp(pathBase);
        await app.CreateFeedAsync("internal");
        await WebUiSession.SeedLocalUserAsync(app, Username, Password);
        using var session = await WebUiSession.SignInAsync(app, Username, Password);

        using var forbidden = await session.Client.GetAsync($"{pathBase}/feeds/internal/");
        using var missing = await session.Client.GetAsync($"{pathBase}/feeds/nope/");

        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private PaGettoApplication CreateApp(string pathBase)
    {
        return new PaGettoApplication(_output, inMemoryConfiguration: dict =>
        {
            dict["Authentication:Mode"] = "Local";
            if (pathBase != null)
            {
                dict["PathBase"] = pathBase;
            }
        });
    }
}
