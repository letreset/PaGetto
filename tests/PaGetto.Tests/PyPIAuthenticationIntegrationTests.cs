using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
/// The package index of a PyPI feed with user accounts: pip, uv and twine send Basic credentials,
/// normally the username <c>__token__</c> with a personal access token.
/// </summary>
public class PyPIAuthenticationIntegrationTests : IDisposable
{
    private const string Feed = "python";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public PyPIAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("feeds/python/pypi/simple/")]
    [InlineData("feeds/python/pypi/simple/my-package/")]
    [InlineData("feeds/python/pypi/files/my-package/1.0/my_package-1.0.tar.gz")]
    // Feeds that don't exist answer the same, so feeds can't be probed.
    [InlineData("feeds/missing/pypi/simple/my-package/")]
    public async Task AnonymousRequestIsChallengedForBasicCredentials(string path)
    {
        await _app.CreatePyPIFeedAsync(Feed);

        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic realm=\"PaGetto\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Theory]
    [InlineData("__token__")]
    [InlineData("publisher")]
    public async Task UserWithPushPermissionUploadsWithAToken(string username)
    {
        var feed = await _app.CreatePyPIFeedAsync(Feed);
        await SignInAsync(feed, "publisher", canPull: true, canPush: true, username);

        var wheel = await _client.PublishPythonAsync(Feed, "my-package", "1.0");
        var download = await _client.GetByteArrayAsync(PyPITestClient.FileUrl(Feed, "my-package", "1.0", PyPITestClient.WheelFileName("my-package", "1.0")));

        Assert.Equal(wheel, download);
    }

    [Fact]
    public async Task ATokenWithAnotherUsersNameIsRejected()
    {
        var feed = await _app.CreatePyPIFeedAsync(Feed);
        await SignInAsync(feed, "publisher", canPull: true, canPush: true, username: "someone-else");

        using var response = await _client.GetAsync(PyPITestClient.SimpleUrl(Feed));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PullOnlyUserCanInstallButNotUpload()
    {
        var feed = await _app.CreatePyPIFeedAsync(Feed);
        await SignInAsync(feed, "publisher", canPull: true, canPush: true);
        await _client.PublishPythonAsync(Feed, "my-package", "1.0");

        await SignInAsync(feed, "reader", canPull: true, canPush: false);
        using var index = await _client.GetAsync(PyPITestClient.ProjectUrl(Feed, "my-package"));
        using var upload = await _client.UploadPythonAsync(Feed, "my-package", "2.0", PyPITestClient.SdistFileName("my-package", "2.0"), PyPITestClient.Sdist("my-package", "2.0"));

        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
        Assert.Contains("permission", upload.ReasonPhrase);
    }

    [Fact]
    public async Task TokenOfAUserWithoutPullPermissionIsNotFound()
    {
        var feed = await _app.CreatePyPIFeedAsync(Feed);
        await SignInAsync(feed, "stranger", canPull: false, canPush: false);

        using var response = await _client.GetAsync(PyPITestClient.SimpleUrl(Feed));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Creates a local account with the given permissions on the feed, and sends a personal access
    /// token of it with Basic authentication from now on, like ~/.pypirc.
    /// </summary>
    private async Task SignInAsync(Feed feed, string accountName, bool canPull, bool canPush, string username = FeedAuthenticationService.TokenUsername)
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

        var token = await tokenService.CreateTokenAsync(user.Id, "pypi", DateTime.UtcNow.AddDays(1), CancellationToken.None);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{token.PlaintextToken}")));
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
