using System;
using System.Net;
using System.Net.Http;
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
/// The gem server of a RubyGems feed with user accounts: Bundler and gem install send Basic
/// credentials with a personal access token as the password, and gem push and gem yank send the
/// token alone as the Authorization header.
/// </summary>
public class RubyGemsAuthenticationIntegrationTests : IDisposable
{
    private const string Feed = "ruby";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public RubyGemsAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("feeds/ruby/rubygems/versions")]
    [InlineData("feeds/ruby/rubygems/info/my_gem")]
    [InlineData("feeds/ruby/rubygems/gems/my_gem-1.0.0.gem")]
    // Feeds that don't exist answer the same, so feeds can't be probed.
    [InlineData("feeds/missing/rubygems/versions")]
    public async Task AnonymousRequestIsChallengedForCredentials(string path)
    {
        await _app.CreateRubyGemsFeedAsync(Feed);

        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic realm=\"PaGetto\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task PublisherPushesWithATokenAndReadersInstallWithIt()
    {
        var feed = await _app.CreateRubyGemsFeedAsync(Feed);
        var publisherToken = await CreateUserAsync(feed, "publisher", canPull: true, canPush: true);
        var readerToken = await CreateUserAsync(feed, "reader", canPull: true);
        var gem = RubyGemBuilder.Create("my_gem", "1.0.0");

        using var push = await _client.PushGemAsync(Feed, gem, apiKey: publisherToken);
        using var readerPush = await _client.PushGemAsync(Feed, RubyGemBuilder.Create("my_gem", "2.0.0"), apiKey: readerToken);

        Assert.Equal(HttpStatusCode.OK, push.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, readerPush.StatusCode);
        Assert.Contains("permission", await readerPush.Content.ReadAsStringAsync());

        _client.UseBasicCredentials("__token__", readerToken);
        using var versions = await _client.GetAsync(RubyGemsTestClient.SourceUrl(Feed) + "versions");
        using var download = await _client.GetAsync(RubyGemsTestClient.SourceUrl(Feed) + "gems/my_gem-1.0.0.gem");

        Assert.Equal(HttpStatusCode.OK, versions.StatusCode);
        Assert.Equal(gem, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task YankingTakesTheDeletePermission()
    {
        var feed = await _app.CreateRubyGemsFeedAsync(Feed);
        var publisherToken = await CreateUserAsync(feed, "publisher", canPull: true, canPush: true);
        var maintainerToken = await CreateUserAsync(feed, "maintainer", canPull: true, canDelete: true);
        using (await _client.PushGemAsync(Feed, RubyGemBuilder.Create("my_gem", "1.0.0"), apiKey: publisherToken))
        {
        }

        using var publisherYank = await _client.YankGemAsync(Feed, "my_gem", "1.0.0", apiKey: publisherToken);
        using var maintainerYank = await _client.YankGemAsync(Feed, "my_gem", "1.0.0", apiKey: maintainerToken);

        Assert.Equal(HttpStatusCode.Forbidden, publisherYank.StatusCode);
        Assert.Equal(HttpStatusCode.OK, maintainerYank.StatusCode);
    }

    [Fact]
    public async Task AnInvalidTokenIsRejected()
    {
        await _app.CreateRubyGemsFeedAsync(Feed);

        using var push = await _client.PushGemAsync(Feed, RubyGemBuilder.Create("my_gem", "1.0.0"), apiKey: "bg_not-a-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, push.StatusCode);
    }

    [Fact]
    public async Task TokenOfAUserWithoutPullPermissionIsNotFound()
    {
        var feed = await _app.CreateRubyGemsFeedAsync(Feed);
        _client.UseBasicCredentials("__token__", await CreateUserAsync(feed, "stranger", canPull: false));

        using var response = await _client.GetAsync(RubyGemsTestClient.SourceUrl(Feed) + "versions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Creates a local account with the given permissions on the feed.
    /// </summary>
    /// <returns>A personal access token of the account.</returns>
    private async Task<string> CreateUserAsync(Feed feed, string accountName, bool canPull, bool canPush = false, bool canDelete = false)
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

        var token = await tokenService.CreateTokenAsync(user.Id, "gem", DateTime.UtcNow.AddDays(1), CancellationToken.None);
        return token.PlaintextToken;
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
