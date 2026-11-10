using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Tests.Support;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The pub API with user accounts: the pub client sends a personal access token as a bearer token.
/// </summary>
public class PubAuthenticationIntegrationTests : IDisposable
{
    private const string Feed = "dart";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public PubAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("feeds/dart/api/packages/my_package")]
    [InlineData("feeds/dart/api/packages/versions/new")]
    [InlineData("feeds/dart/packages/my_package/versions/1.0.0.tar.gz")]
    // Feeds that don't exist, or that are NuGet feeds, answer the same, so feeds can't be probed.
    [InlineData("feeds/missing/api/packages/my_package")]
    [InlineData("api/packages/my_package")]
    public async Task AnonymousRequestGetsABearerChallengeWithAMessage(string path)
    {
        await _app.CreatePubFeedAsync(Feed);

        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains("realm=\"pub\"", challenge.Parameter);
        Assert.Contains("message=\"", challenge.Parameter);
        Assert.Contains("dart pub token add", challenge.Parameter);
    }

    [Fact]
    public async Task AnonymousNuGetRequestOnADartFeedStillGetsTheBasicChallenge()
    {
        await _app.CreatePubFeedAsync(Feed);

        using var response = await _client.GetAsync($"feeds/{Feed}/v3/search");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Fact]
    public async Task InvalidTokenIsUnauthorized()
    {
        await _app.CreatePubFeedAsync(Feed);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "bg_not-a-real-token");

        using var response = await _client.GetAsync($"feeds/{Feed}/api/packages/my_package");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenOfAUserWithoutPullPermissionIsNotFound()
    {
        var feed = await _app.CreatePubFeedAsync(Feed);
        await SignInWithTokenAsync(feed, canPull: false, canPush: false);

        using var response = await _client.GetAsync($"feeds/{Feed}/api/packages/my_package");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PullOnlyUserCanReadButNotPublish()
    {
        var feed = await _app.CreatePubFeedAsync(Feed);
        await SignInWithTokenAsync(feed, canPull: true, canPush: false);

        using var read = await _client.GetAsync($"feeds/{Feed}/api/packages/my_package");
        using var publish = await _client.GetAsync($"feeds/{Feed}/api/packages/versions/new");

        // The package doesn't exist, but the request was let through to find that out.
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(PubTestClient.PubMediaType, read.Content.Headers.ContentType?.MediaType);

        // 403 with a message: the client keeps its token and shows why publishing failed.
        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
        var challenge = Assert.Single(publish.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains("permission to publish", challenge.Parameter);
    }

    [Fact]
    public async Task UserWithPushPermissionCanPublishAndDownload()
    {
        var feed = await _app.CreatePubFeedAsync(Feed);
        await SignInWithTokenAsync(feed, canPull: true, canPush: true);

        using var finalize = await _client.PublishAsync(Feed, PubArchiveBuilder.Create("my_package", "1.0.0"));
        using var listing = await _client.GetAsync($"feeds/{Feed}/api/packages/my_package");
        using var download = await _client.GetAsync($"feeds/{Feed}/packages/my_package/versions/1.0.0.tar.gz");

        Assert.Equal(HttpStatusCode.OK, finalize.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Fact]
    public async Task SignedInUserGetsNotFoundForTheOtherEcosystem()
    {
        var feed = await _app.CreatePubFeedAsync(Feed);
        await SignInWithTokenAsync(feed, canPull: true, canPush: true);

        using var nugetOnDart = await _client.GetAsync($"feeds/{Feed}/v3/search");
        using var nugetIndexOnDart = await _client.GetAsync($"feeds/{Feed}/v3/index.json");

        Assert.Equal(HttpStatusCode.NotFound, nugetOnDart.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nugetIndexOnDart.StatusCode);
    }

    /// <summary>
    /// Creates a local account with the given permissions on the feed, and sends its personal
    /// access token as a bearer token from now on.
    /// </summary>
    private async Task SignInWithTokenAsync(Feed feed, bool canPull, bool canPush)
    {
        using var scope = _app.Services.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var user = await userService.CreateLocalUserAsync(
            "dartuser", "Dart User", null, "TestPassword123!", canLoginToUI: false, createdByUserId: null, CancellationToken.None);

        if (canPull || canPush)
        {
            await permissionService.GrantPermissionAsync(
                user.Id, PrincipalType.User, feed.Id, canPush: canPush, canPull: canPull, CancellationToken.None);
        }

        var token = await tokenService.CreateTokenAsync(user.Id, "pub", DateTime.UtcNow.AddDays(1), CancellationToken.None);

        // The pub client only accepts tokens made of these characters.
        Assert.Matches("^[a-zA-Z0-9._~+/=-]+$", token.PlaintextToken);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.PlaintextToken);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
