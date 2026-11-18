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
/// The Ivy repository with user accounts: sbt sends the username and a personal access token of its
/// credentials with Basic authentication, once it is challenged with their realm.
/// </summary>
public class IvyAuthenticationIntegrationTests : IDisposable
{
    private const string Feed = "sbt";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public IvyAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("feeds/sbt/ivy/com.example/sbt-tool/scala_2.12/sbt_1.0/")]
    [InlineData("feeds/sbt/ivy/com.example/sbt-tool/scala_2.12/sbt_1.0/1.0.0/ivys/ivy.xml")]
    // Feeds that don't exist answer the same, so feeds can't be probed.
    [InlineData("feeds/missing/ivy/com.example/sbt-tool/scala_2.12/sbt_1.0/1.0.0/ivys/ivy.xml")]
    public async Task AnonymousRequestIsChallengedForBasicCredentials(string path)
    {
        await _app.CreateIvyFeedAsync(Feed);

        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic realm=\"PaGetto\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task TokenOfAUserWithoutPullPermissionIsNotFound()
    {
        var feed = await _app.CreateIvyFeedAsync(Feed);
        await SignInAsync(feed, "reader", canPull: false, canPush: false);

        using var response = await _client.GetAsync(IvyTestClient.Url(Feed, "com.example/sbt-tool/scala_2.12/sbt_1.0/"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PullOnlyUserCanDownloadButNotPublish()
    {
        var feed = await _app.CreateIvyFeedAsync(Feed);
        await SignInAsync(feed, "publisher", canPull: true, canPush: true);
        var jar = await _client.PublishPluginAsync(Feed, "sbt-tool", "1.0.0");

        await SignInAsync(feed, "reader", canPull: true, canPush: false);
        var download = await _client.GetByteArrayAsync(IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "1.0.0", "jars/sbt-tool.jar"));
        using var publish = await _client.PutBytesAsync(IvyTestClient.PluginFileUrl(Feed, "sbt-tool", "2.0.0", "jars/sbt-tool.jar"), [1]);

        Assert.Equal(jar, download);
        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
        Assert.Contains("permission", publish.ReasonPhrase);
    }

    [Fact]
    public async Task SignedInUserGetsNotFoundForTheOtherEcosystems()
    {
        var feed = await _app.CreateIvyFeedAsync(Feed);
        await SignInAsync(feed, "reader", canPull: true, canPush: false);

        using var nugetOnIvy = await _client.GetAsync($"feeds/{Feed}/v3/index.json");
        using var mavenOnIvy = await _client.GetAsync($"feeds/{Feed}/maven2/com/example/my-lib/maven-metadata.xml");

        Assert.Equal(HttpStatusCode.NotFound, nugetOnIvy.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, mavenOnIvy.StatusCode);
    }

    /// <summary>
    /// Creates a local account with the given permissions on the feed, and sends its username and
    /// personal access token with Basic authentication from now on, like sbt's credentials.
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

        var token = await tokenService.CreateTokenAsync(user.Id, "sbt", DateTime.UtcNow.AddDays(1), CancellationToken.None);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{token.PlaintextToken}")));
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
