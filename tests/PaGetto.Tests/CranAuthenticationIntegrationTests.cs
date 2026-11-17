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
/// The CRAN repository of a CRAN feed with user accounts: R sends the credentials of the
/// repository URL as Basic authentication, with a personal access token as the password.
/// </summary>
public class CranAuthenticationIntegrationTests : IDisposable
{
    private const string Feed = "r";
    private const string Package = "hello";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public CranAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("feeds/r/cran/src/contrib/PACKAGES.gz")]
    [InlineData("feeds/r/cran/src/contrib/hello_1.0.tar.gz")]
    [InlineData("feeds/r/cran/src/contrib/Archive/hello/")]
    [InlineData("feeds/r/cran/bin/windows/contrib/4.4/PACKAGES")]
    // Feeds that don't exist answer the same, so feeds can't be probed.
    [InlineData("feeds/missing/cran/src/contrib/PACKAGES.gz")]
    public async Task AnonymousRequestIsChallengedForCredentials(string path)
    {
        await _app.CreateCranFeedAsync(Feed);

        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("__token__")]
    [InlineData("publisher")]
    public async Task UserWithPushPermissionUploadsAndInstallsWithAToken(string username)
    {
        var feed = await _app.CreateCranFeedAsync(Feed);
        var token = await CreateUserAsync(feed, "publisher", canPull: true, canPush: true);
        _client.UseBasicCredentials(username, token);

        using var upload = await _client.UploadCranPackageAsync(Feed, Package, "1.0");
        using var index = await _client.GetAsync($"feeds/{Feed}/cran/src/contrib/PACKAGES.gz");
        using var package = await _client.GetAsync($"feeds/{Feed}/cran/src/contrib/hello_1.0.tar.gz");

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Equal(HttpStatusCode.OK, package.StatusCode);
    }

    [Fact]
    public async Task PullOnlyUserCanInstallButNotUpload()
    {
        var feed = await _app.CreateCranFeedAsync(Feed);
        _client.UseBasicCredentials("__token__", await CreateUserAsync(feed, "publisher", canPull: true, canPush: true));
        using (await _client.UploadCranPackageAsync(Feed, Package, "1.0"))
        {
        }

        _client.UseBasicCredentials("__token__", await CreateUserAsync(feed, "reader", canPull: true));
        using var package = await _client.GetAsync($"feeds/{Feed}/cran/src/contrib/hello_1.0.tar.gz");
        using var upload = await _client.UploadCranPackageAsync(Feed, Package, "2.0");

        Assert.Equal(HttpStatusCode.OK, package.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
        Assert.Contains("permission", await upload.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TokenOfAUserWithoutPullPermissionIsNotFound()
    {
        var feed = await _app.CreateCranFeedAsync(Feed);
        _client.UseBasicCredentials("__token__", await CreateUserAsync(feed, "stranger", canPull: false));

        using var response = await _client.GetAsync($"feeds/{Feed}/cran/src/contrib/PACKAGES.gz");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnInvalidTokenIsRejected()
    {
        await _app.CreateCranFeedAsync(Feed);
        _client.UseBasicCredentials("__token__", "bg_not-a-real-token");

        using var response = await _client.GetAsync($"feeds/{Feed}/cran/src/contrib/PACKAGES.gz");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic realm=\"PaGetto\"", response.Headers.WwwAuthenticate.ToString());
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

        var token = await tokenService.CreateTokenAsync(user.Id, "r", DateTime.UtcNow.AddDays(1), CancellationToken.None);
        return token.PlaintextToken;
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
