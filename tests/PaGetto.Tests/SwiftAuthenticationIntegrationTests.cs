using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
/// The Swift package registry API of a Swift feed with user accounts: swift package-registry login
/// stores a personal access token, which Swift Package Manager sends as a Bearer token, or a
/// username and password, sent as Basic authentication.
/// </summary>
public class SwiftAuthenticationIntegrationTests : IDisposable
{
    private const string Feed = "swift";
    private const string Package = "mona.LinkedList";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public SwiftAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("GET", "feeds/swift/swift/mona/LinkedList")]
    [InlineData("GET", "feeds/swift/swift/mona/LinkedList/1.0.0")]
    [InlineData("GET", "feeds/swift/swift/mona/LinkedList/1.0.0.zip")]
    [InlineData("GET", "feeds/swift/swift/mona/LinkedList/1.0.0/Package.swift")]
    [InlineData("POST", "feeds/swift/swift/login")]
    // Feeds that don't exist answer the same, so feeds can't be probed.
    [InlineData("GET", "feeds/missing/swift/mona/LinkedList")]
    public async Task AnonymousRequestIsRefusedWithoutAChallenge(string method, string path)
    {
        await _app.CreateSwiftFeedAsync(Feed);

        using var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        // Swift Package Manager hangs on Linux on a 401 with a WWW-Authenticate header, and shows the detail.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("Content-Version")));
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            $$"""{"detail":"Authentication is required. Sign in with: swift package-registry login http://localhost/feeds/{{path.Split('/')[1]}}/swift/login --token YOUR_TOKEN"}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UserWithPushPermissionLogsInPublishesAndInstallsWithAToken()
    {
        var feed = await _app.CreateSwiftFeedAsync(Feed);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserAsync(feed, "publisher", canPull: true, canPush: true));

        using var login = await _client.PostAsync($"feeds/{Feed}/swift/login", null);
        using var publish = await _client.PublishAsync(Feed, Package, "1.0.0", SwiftTestClient.SourceArchive());
        using var release = await _client.GetAsync(SwiftTestClient.ReleaseUrl(Feed, Package, "1.0.0"));
        using var zip = await _client.GetAsync(SwiftTestClient.ReleaseUrl(Feed, Package, "1.0.0") + ".zip");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        Assert.Equal(HttpStatusCode.OK, zip.StatusCode);
    }

    [Fact]
    public async Task UsernameAndPasswordAreAcceptedToo()
    {
        var feed = await _app.CreateSwiftFeedAsync(Feed);
        await CreateUserAsync(feed, "publisher", canPull: true, canPush: true);
        _client.UseBasicCredentials("publisher", "TestPassword123!");

        using var login = await _client.PostAsync($"feeds/{Feed}/swift/login", null);
        using var publish = await _client.PublishAsync(Feed, Package, "1.0.0", SwiftTestClient.SourceArchive());

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
    }

    [Fact]
    public async Task PullOnlyUserCanInstallButNotPublish()
    {
        var feed = await _app.CreateSwiftFeedAsync(Feed);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserAsync(feed, "publisher", canPull: true, canPush: true));
        using (await _client.PublishAsync(Feed, Package, "1.0.0", SwiftTestClient.SourceArchive()))
        {
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserAsync(feed, "reader", canPull: true));
        using var zip = await _client.GetAsync(SwiftTestClient.ReleaseUrl(Feed, Package, "1.0.0") + ".zip");
        using var publish = await _client.PublishAsync(Feed, Package, "2.0.0", SwiftTestClient.SourceArchive());

        Assert.Equal(HttpStatusCode.OK, zip.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
        Assert.Contains("permission", await publish.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TokenOfAUserWithoutPullPermissionIsNotFound()
    {
        var feed = await _app.CreateSwiftFeedAsync(Feed);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserAsync(feed, "stranger", canPull: false));

        using var response = await _client.GetAsync(SwiftTestClient.ReleaseUrl(Feed, Package));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnInvalidTokenIsRejected()
    {
        await _app.CreateSwiftFeedAsync(Feed);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "bg_not-a-real-token");

        using var login = await _client.PostAsync($"feeds/{Feed}/swift/login", null);

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
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

        var token = await tokenService.CreateTokenAsync(user.Id, "swift", DateTime.UtcNow.AddDays(1), CancellationToken.None);
        return token.PlaintextToken;
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
