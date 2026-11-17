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
/// The spec repository of a CocoaPods feed with user accounts: CocoaPods and curl send the
/// credentials of ~/.netrc as Basic authentication, with a personal access token as the password.
/// </summary>
public class CocoaPodsAuthenticationIntegrationTests : IDisposable
{
    private const string Feed = "pods";
    private const string Pod = "MyLib";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public CocoaPodsAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("feeds/pods/cocoapods/CocoaPods-version.yml")]
    [InlineData("feeds/pods/cocoapods/all_pods_versions_2_f_4.txt")]
    [InlineData("feeds/pods/cocoapods/Specs/2/f/4/MyLib/1.0.0/MyLib.podspec.json")]
    [InlineData("feeds/pods/cocoapods/sources/MyLib/1.0.0/MyLib-1.0.0.zip")]
    // Feeds that don't exist answer the same, so feeds can't be probed.
    [InlineData("feeds/missing/cocoapods/CocoaPods-version.yml")]
    public async Task AnonymousRequestIsChallengedForCredentials(string path)
    {
        await _app.CreateCocoaPodsFeedAsync(Feed);

        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic realm=\"PaGetto\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Theory]
    [InlineData("__token__")]
    [InlineData("publisher")]
    public async Task UserWithPushPermissionPublishesAndInstallsWithAToken(string username)
    {
        var feed = await _app.CreateCocoaPodsFeedAsync(Feed);
        var token = await CreateUserAsync(feed, "publisher", canPull: true, canPush: true);
        _client.UseBasicCredentials(username, token);

        using var publish = await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0"), CocoaPodsTestClient.SourceZip(), "MyLib.zip");
        using var version = await _client.GetAsync($"feeds/{Feed}/cocoapods/CocoaPods-version.yml");
        using var index = await _client.GetAsync(CocoaPodsTestClient.IndexUrl(Feed, Pod));
        using var podspec = await _client.GetAsync(CocoaPodsTestClient.PodspecUrl(Feed, Pod, "1.0.0"));
        using var source = await _client.GetAsync($"feeds/{Feed}/cocoapods/sources/MyLib/1.0.0/MyLib-1.0.0.zip");

        Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        Assert.Equal(HttpStatusCode.OK, version.StatusCode);
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Equal(HttpStatusCode.OK, podspec.StatusCode);
        Assert.Equal(HttpStatusCode.OK, source.StatusCode);
    }

    [Fact]
    public async Task PullOnlyUserCanInstallButNotPublish()
    {
        var feed = await _app.CreateCocoaPodsFeedAsync(Feed);
        _client.UseBasicCredentials("__token__", await CreateUserAsync(feed, "publisher", canPull: true, canPush: true));
        using (await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "1.0.0")))
        {
        }

        _client.UseBasicCredentials("__token__", await CreateUserAsync(feed, "reader", canPull: true));
        using var podspec = await _client.GetAsync(CocoaPodsTestClient.PodspecUrl(Feed, Pod, "1.0.0"));
        using var publish = await _client.PublishAsync(Feed, CocoaPodsTestClient.Podspec(Pod, "2.0.0"));

        Assert.Equal(HttpStatusCode.OK, podspec.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
        Assert.Contains("permission", await publish.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TokenOfAUserWithoutPullPermissionIsNotFound()
    {
        var feed = await _app.CreateCocoaPodsFeedAsync(Feed);
        _client.UseBasicCredentials("__token__", await CreateUserAsync(feed, "stranger", canPull: false));

        using var response = await _client.GetAsync($"feeds/{Feed}/cocoapods/CocoaPods-version.yml");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnInvalidTokenIsRejected()
    {
        await _app.CreateCocoaPodsFeedAsync(Feed);
        _client.UseBasicCredentials("__token__", "bg_not-a-real-token");

        using var response = await _client.GetAsync($"feeds/{Feed}/cocoapods/CocoaPods-version.yml");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Creates a local account with the given permissions on the feed.
    /// </summary>
    /// <returns>A personal access token of the account.</returns>
    private async Task<string> CreateUserAsync(Feed feed, string accountName, bool canPull, bool canPush = false)
    {
        using var scope = _app.Services.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var user = await userService.CreateLocalUserAsync(
            accountName, accountName, null, "TestPassword123!", canLoginToUI: false, createdByUserId: null, CancellationToken.None);

        if (canPull || canPush)
        {
            await permissionService.GrantPermissionAsync(
                user.Id, PrincipalType.User, feed.Id, canPush: canPush, canPull: canPull, CancellationToken.None);
        }

        var token = await tokenService.CreateTokenAsync(user.Id, "cocoapods", DateTime.UtcNow.AddDays(1), CancellationToken.None);
        return token.PlaintextToken;
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
