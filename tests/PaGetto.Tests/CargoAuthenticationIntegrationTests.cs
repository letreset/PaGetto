using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
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
/// The registry of a Cargo feed with user accounts: Cargo sends a personal access token as the
/// whole <c>Authorization</c> header, without a scheme.
/// </summary>
public class CargoAuthenticationIntegrationTests : IDisposable
{
    private const string Feed = "rust";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public CargoAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("feeds/rust/cargo/index/config.json")]
    [InlineData("feeds/rust/cargo/index/my/_c/my_crate")]
    [InlineData("feeds/rust/cargo/api/v1/crates/my_crate/1.0.0/download")]
    // Feeds that don't exist answer the same, so feeds can't be probed.
    [InlineData("feeds/missing/cargo/index/config.json")]
    public async Task AnonymousRequestIsChallengedForAToken(string path)
    {
        await _app.CreateCargoFeedAsync(Feed);

        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith("Cargo login_url=", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task ConfigTellsCargoToSendItsTokenWithEveryRequest()
    {
        var feed = await _app.CreateCargoFeedAsync(Feed);
        await SignInAsync(feed, "reader", canPull: true);

        using var response = await _client.GetAsync(CargoTestClient.IndexUrl(Feed) + "config.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var config = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(config.RootElement.GetProperty("auth-required").GetBoolean());
    }

    [Fact]
    public async Task UserWithPushPermissionPublishesAndInstallsWithAToken()
    {
        var feed = await _app.CreateCargoFeedAsync(Feed);
        await SignInAsync(feed, "publisher", canPull: true, canPush: true);

        using var publish = await _client.PublishCrateAsync(Feed, "my_crate", "1.0.0");
        using var index = await _client.GetAsync(CargoTestClient.IndexFileUrl(Feed, "my_crate"));
        using var download = await _client.GetAsync(CargoTestClient.DownloadUrl(Feed, "my_crate", "1.0.0"));

        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Fact]
    public async Task ATokenWithABearerSchemeWorksToo()
    {
        var feed = await _app.CreateCargoFeedAsync(Feed);
        var token = await SignInAsync(feed, "reader", canPull: true);
        _client.DefaultRequestHeaders.Remove("Authorization");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _client.GetAsync(CargoTestClient.IndexUrl(Feed) + "config.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnInvalidTokenIsRejected()
    {
        await _app.CreateCargoFeedAsync(Feed);
        _client.UseCargoToken("bg_not-a-real-token");

        using var response = await _client.GetAsync(CargoTestClient.IndexUrl(Feed) + "config.json");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PullOnlyUserCanInstallButNotPublishOrYank()
    {
        var feed = await _app.CreateCargoFeedAsync(Feed);
        await SignInAsync(feed, "publisher", canPull: true, canPush: true);
        using (await _client.PublishCrateAsync(Feed, "my_crate", "1.0.0"))
        {
        }

        await SignInAsync(feed, "reader", canPull: true);
        using var index = await _client.GetAsync(CargoTestClient.IndexFileUrl(Feed, "my_crate"));
        using var publish = await _client.PublishCrateAsync(Feed, "my_crate", "2.0.0");
        using var yank = await _client.DeleteAsync($"feeds/{Feed}/cargo/api/v1/crates/my_crate/1.0.0/yank");

        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
        Assert.Contains("permission", await publish.ReadCargoErrorAsync());
        Assert.Equal(HttpStatusCode.Forbidden, yank.StatusCode);
    }

    [Fact]
    public async Task YankingTakesTheDeletePermission()
    {
        var feed = await _app.CreateCargoFeedAsync(Feed);
        await SignInAsync(feed, "publisher", canPull: true, canPush: true);
        using (await _client.PublishCrateAsync(Feed, "my_crate", "1.0.0"))
        {
        }

        await SignInAsync(feed, "maintainer", canPull: true, canDelete: true);
        using var yank = await _client.DeleteAsync($"feeds/{Feed}/cargo/api/v1/crates/my_crate/1.0.0/yank");

        Assert.Equal(HttpStatusCode.OK, yank.StatusCode);
    }

    [Fact]
    public async Task TokenOfAUserWithoutPullPermissionIsNotFound()
    {
        var feed = await _app.CreateCargoFeedAsync(Feed);
        await SignInAsync(feed, "stranger", canPull: false);

        using var response = await _client.GetAsync(CargoTestClient.IndexUrl(Feed) + "config.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Creates a local account with the given permissions on the feed, and sends a personal access
    /// token of it the way Cargo does from now on.
    /// </summary>
    private async Task<string> SignInAsync(Feed feed, string accountName, bool canPull, bool canPush = false, bool canDelete = false)
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

        var token = await tokenService.CreateTokenAsync(user.Id, "cargo", DateTime.UtcNow.AddDays(1), CancellationToken.None);

        _client.UseCargoToken(token.PlaintextToken);
        return token.PlaintextToken;
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
