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
/// The Maven repository with user accounts: Maven and Gradle send the username and a personal
/// access token with Basic authentication, once they are challenged.
/// </summary>
public class MavenAuthenticationIntegrationTests : IDisposable
{
    private const string Feed = "maven";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public MavenAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("feeds/maven/maven2/com/example/my-lib/maven-metadata.xml")]
    [InlineData("feeds/maven/maven2/com/example/my-lib/1.0/my-lib-1.0.jar")]
    // Feeds that don't exist answer the same, so feeds can't be probed.
    [InlineData("feeds/missing/maven2/com/example/my-lib/maven-metadata.xml")]
    public async Task AnonymousRequestIsChallengedForBasicCredentials(string path)
    {
        await _app.CreateMavenFeedAsync(Feed);

        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic realm=\"PaGetto\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task TokenOfAUserWithoutPullPermissionIsNotFound()
    {
        var feed = await _app.CreateMavenFeedAsync(Feed);
        await SignInAsync(feed, "reader", canPull: false, canPush: false);

        using var response = await _client.GetAsync(MavenTestClient.Url(Feed, "com/example/my-lib/maven-metadata.xml"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PullOnlyUserCanDownloadButNotDeploy()
    {
        var feed = await _app.CreateMavenFeedAsync(Feed);
        await SignInAsync(feed, "publisher", canPull: true, canPush: true);
        var jar = await _client.DeployAsync(Feed, "com.example", "my-lib", "1.0");

        await SignInAsync(feed, "reader", canPull: true, canPush: false);
        var download = await _client.GetByteArrayAsync(MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "1.0", "my-lib-1.0.jar"));
        using var deploy = await _client.PutBytesAsync(MavenTestClient.FileUrl(Feed, "com.example", "my-lib", "2.0", "my-lib-2.0.jar"), [1]);

        Assert.Equal(jar, download);
        Assert.Equal(HttpStatusCode.Forbidden, deploy.StatusCode);
        Assert.Contains("permission", deploy.ReasonPhrase);
    }

    [Fact]
    public async Task SignedInUserGetsNotFoundForTheOtherEcosystems()
    {
        var feed = await _app.CreateMavenFeedAsync(Feed);
        await SignInAsync(feed, "reader", canPull: true, canPush: false);

        using var nugetOnMaven = await _client.GetAsync($"feeds/{Feed}/v3/index.json");
        using var npmOnMaven = await _client.GetAsync($"feeds/{Feed}/npm/my-package");

        Assert.Equal(HttpStatusCode.NotFound, nugetOnMaven.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, npmOnMaven.StatusCode);
    }

    /// <summary>
    /// Creates a local account with the given permissions on the feed, and sends its username and
    /// personal access token with Basic authentication from now on, like Maven's settings.xml.
    /// </summary>
    private async Task SignInAsync(Feed feed, string username, bool canPull, bool canPush)
    {
        using var scope = _app.Services.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var user = await userService.CreateLocalUserAsync(
            username, username, null, "TestPassword123!", canLoginToUI: false, createdByUserId: null, CancellationToken.None);

        if (canPull || canPush)
        {
            await permissionService.GrantPermissionAsync(
                user.Id, PrincipalType.User, feed.Id, canPush: canPush, canPull: canPull, CancellationToken.None);
        }

        var token = await tokenService.CreateTokenAsync(user.Id, "maven", DateTime.UtcNow.AddDays(1), CancellationToken.None);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{token.PlaintextToken}")));
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
