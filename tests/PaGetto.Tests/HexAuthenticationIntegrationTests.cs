using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The repository and API of a Hex feed with user accounts: Mix and Rebar3 send a personal access
/// token as the whole <c>Authorization</c> header, without a scheme.
/// </summary>
public class HexAuthenticationIntegrationTests : IDisposable
{
    private const string Feed = "hex";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public HexAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("feeds/hex/hex/names")]
    [InlineData("feeds/hex/hex/packages/demo")]
    [InlineData("feeds/hex/hex/tarballs/demo-1.0.0.tar")]
    [InlineData("feeds/hex/hex/public_key")]
    [InlineData("feeds/hex/hex/api/users/me")]
    // Feeds that don't exist answer the same, so feeds can't be probed.
    [InlineData("feeds/missing/hex/names")]
    public async Task AnonymousRequestIsChallenged(string path)
    {
        await _app.CreateHexFeedAsync(Feed);

        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic realm=\"PaGetto\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task AnonymousPublishIsChallenged()
    {
        await _app.CreateHexFeedAsync(Feed);

        using var response = await _client.PublishHexAsync(Feed, "demo", "1.0.0");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MixAsksHexPmWithoutCredentialsAndIsToldNo()
    {
        await _app.CreateHexFeedAsync(Feed);

        using var response = await _client.GetAsync($"{HexTestClient.RepositoryUrl(Feed)}/api/repos/hexpm/packages/demo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("not hexpm", await response.ReadHexErrorAsync());
    }

    [Fact]
    public async Task UserWithPushPermissionPublishesAndInstallsWithAToken()
    {
        var feed = await _app.CreateHexFeedAsync(Feed);
        await SignInAsync(feed, "publisher", canPull: true, canPush: true);

        using var publish = await _client.PublishHexAsync(Feed, "demo", "1.0.0");
        using var registry = await _client.GetAsync($"{HexTestClient.RepositoryUrl(Feed)}/packages/demo");
        using var download = await _client.GetAsync(HexTestClient.TarballUrl(Feed, "demo", "1.0.0"));
        using var me = await _client.GetAsync($"{HexTestClient.RepositoryUrl(Feed)}/api/users/me");

        Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        Assert.Equal(HttpStatusCode.OK, registry.StatusCode);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("publisher", (await me.ReadHexTermAsync())["username"]);
    }

    [Fact]
    public async Task AnInvalidTokenIsRejected()
    {
        await _app.CreateHexFeedAsync(Feed);
        _client.UseHexKey("bg_not-a-real-token");

        using var response = await _client.GetAsync($"{HexTestClient.RepositoryUrl(Feed)}/names");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PullOnlyUserCanInstallButNotPublishOrRetire()
    {
        var feed = await _app.CreateHexFeedAsync(Feed);
        await SignInAsync(feed, "publisher", canPull: true, canPush: true);
        using (await _client.PublishHexAsync(Feed, "demo", "1.0.0"))
        {
        }

        await SignInAsync(feed, "reader", canPull: true);
        using var download = await _client.GetAsync(HexTestClient.TarballUrl(Feed, "demo", "1.0.0"));
        using var publish = await _client.PublishHexAsync(Feed, "demo", "2.0.0");
        using var retire = await _client.RetireHexAsync(Feed, "demo", "1.0.0", "other", "No");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
        Assert.Contains("permission", await publish.ReadHexErrorAsync());
        Assert.Equal(HttpStatusCode.Forbidden, retire.StatusCode);
    }

    [Fact]
    public async Task RetiringTakesTheDeletePermission()
    {
        var feed = await _app.CreateHexFeedAsync(Feed);
        await SignInAsync(feed, "publisher", canPull: true, canPush: true);
        using (await _client.PublishHexAsync(Feed, "demo", "1.0.0"))
        {
        }

        await SignInAsync(feed, "maintainer", canPull: true, canDelete: true);
        using var retire = await _client.RetireHexAsync(Feed, "demo", "1.0.0", "deprecated", "Old");

        Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);
    }

    [Fact]
    public async Task TokenOfAUserWithoutPullPermissionIsNotFound()
    {
        var feed = await _app.CreateHexFeedAsync(Feed);
        await SignInAsync(feed, "stranger", canPull: false);

        using var response = await _client.GetAsync($"{HexTestClient.RepositoryUrl(Feed)}/names");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Creates a local account with the given permissions on the feed, and sends a personal access
    /// token of it the way Hex does from now on.
    /// </summary>
    private async Task SignInAsync(Feed feed, string accountName, bool canPull, bool canPush = false, bool canDelete = false)
    {
        using var scope = _app.Services.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var user = await userService.CreateLocalUserAsync(
            accountName, accountName, null, "TestPassword123!", canLoginToUI: false, createdByUserId: null, CancellationToken.None);

        if (canPull || canPush || canDelete)
        {
            await permissionService.GrantPermissionAsync(
                user.Id, PrincipalType.User, feed.Id, canPush: canPush, canPull: canPull, CancellationToken.None, canDelete: canDelete);
        }

        var token = await tokenService.CreateTokenAsync(user.Id, "hex", DateTime.UtcNow.AddDays(1), CancellationToken.None);

        _client.UseHexKey(token.PlaintextToken);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
