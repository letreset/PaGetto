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
/// The module proxy of a Go feed with user accounts: the go command sends the credentials of a
/// .netrc file as Basic authentication, with a personal access token as the password.
/// </summary>
public class GoAuthenticationIntegrationTests : IDisposable
{
    private const string Feed = "golang";
    private const string Module = "example.com/tool";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public GoAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("feeds/golang/go/example.com/tool/@v/list")]
    [InlineData("feeds/golang/go/example.com/tool/@v/v1.0.0.zip")]
    // Feeds that don't exist answer the same, so feeds can't be probed.
    [InlineData("feeds/missing/go/example.com/tool/@v/list")]
    public async Task AnonymousRequestIsChallengedForCredentials(string path)
    {
        await _app.CreateGoFeedAsync(Feed);

        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("__token__")]
    [InlineData("publisher")]
    public async Task UserWithPushPermissionUploadsAndDownloadsWithAToken(string username)
    {
        var feed = await _app.CreateGoFeedAsync(Feed);
        var token = await CreateUserAsync(feed, "publisher", canPull: true, canPush: true);
        _client.UseBasicCredentials(username, token);

        using var upload = await _client.UploadModuleAsync(Feed, Module, "v1.0.0");
        using var list = await _client.GetAsync(GoTestClient.ProxyUrl(Feed, Module, "@v/list"));
        using var zip = await _client.GetAsync(GoTestClient.VersionUrl(Feed, Module, "v1.0.0", ".zip"));

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, zip.StatusCode);
    }

    [Fact]
    public async Task PullOnlyUserCanDownloadButNotUpload()
    {
        var feed = await _app.CreateGoFeedAsync(Feed);
        _client.UseBasicCredentials("__token__", await CreateUserAsync(feed, "publisher", canPull: true, canPush: true));
        using (await _client.UploadModuleAsync(Feed, Module, "v1.0.0"))
        {
        }

        _client.UseBasicCredentials("__token__", await CreateUserAsync(feed, "reader", canPull: true));
        using var mod = await _client.GetAsync(GoTestClient.VersionUrl(Feed, Module, "v1.0.0", ".mod"));
        using var upload = await _client.UploadModuleAsync(Feed, Module, "v2.0.0");

        Assert.Equal(HttpStatusCode.OK, mod.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
        Assert.Contains("permission", await upload.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TokenOfAUserWithoutPullPermissionIsNotFound()
    {
        var feed = await _app.CreateGoFeedAsync(Feed);
        _client.UseBasicCredentials("__token__", await CreateUserAsync(feed, "stranger", canPull: false));

        using var response = await _client.GetAsync(GoTestClient.ProxyUrl(Feed, Module, "@v/list"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnInvalidTokenIsRejected()
    {
        await _app.CreateGoFeedAsync(Feed);
        _client.UseBasicCredentials("__token__", "bg_not-a-real-token");

        using var response = await _client.GetAsync(GoTestClient.ProxyUrl(Feed, Module, "@v/list"));

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

        var token = await tokenService.CreateTokenAsync(user.Id, "go", DateTime.UtcNow.AddDays(1), CancellationToken.None);
        return token.PlaintextToken;
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
