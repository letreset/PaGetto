using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
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
/// The npm registry API with user accounts: npm sends the personal access token from .npmrc
/// (<c>_authToken</c>) as a bearer token.
/// </summary>
public class NpmAuthenticationIntegrationTests : IDisposable
{
    private const string Feed = "npm";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public NpmAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("feeds/npm/npm/my-package")]
    [InlineData("feeds/npm/npm/my-package/-/my-package-1.0.0.tgz")]
    [InlineData("feeds/npm/npm/-/whoami")]
    // Feeds that don't exist, or that are of another type, answer the same, so feeds can't be probed.
    [InlineData("feeds/missing/npm/my-package")]
    [InlineData("npm/my-package")]
    public async Task AnonymousRequestIsUnauthorized(string path)
    {
        await _app.CreateNpmFeedAsync(Feed);

        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenOfAUserWithoutPullPermissionIsNotFound()
    {
        var feed = await _app.CreateNpmFeedAsync(Feed);
        await SignInWithTokenAsync(feed, canPull: false, canPush: false, canDelete: false);

        using var response = await _client.GetAsync($"feeds/{Feed}/npm/my-package");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PullOnlyUserCanInstallButNotPublish()
    {
        var feed = await _app.CreateNpmFeedAsync(Feed);
        await PublishAsAnotherUserAsync(feed);
        await SignInWithTokenAsync(feed, canPull: true, canPush: false, canDelete: false);

        using var read = await _client.GetAsync($"feeds/{Feed}/npm/my-package");
        using var download = await _client.GetAsync($"feeds/{Feed}/npm/my-package/-/my-package-1.0.0.tgz");
        using var publish = await _client.PublishAsync(Feed, "my-package", "2.0.0");
        using var tag = await _client.PutJsonAsync($"feeds/{Feed}/npm/-/package/my-package/dist-tags/next", JsonValue.Create("1.0.0"));
        var whoami = await _client.GetJsonObjectAsync($"feeds/{Feed}/npm/-/whoami");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
        Assert.Contains("permission", await publish.ReadNpmErrorAsync());
        Assert.Equal(HttpStatusCode.Forbidden, tag.StatusCode);
        Assert.Equal("npmuser", whoami["username"]?.GetValue<string>());
    }

    [Fact]
    public async Task PushPermissionPublishesAndDeprecatesButDoesNotUnpublish()
    {
        var feed = await _app.CreateNpmFeedAsync(Feed);
        await SignInWithTokenAsync(feed, canPull: true, canPush: true, canDelete: false);

        using (var publish = await _client.PublishAsync(Feed, "@scope/pkg", "1.0.0"))
        {
            Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        }

        using (await _client.PublishAsync(Feed, "@scope/pkg", "2.0.0"))
        {
        }

        var url = NpmTestClient.PackageUrl(Feed, "@scope/pkg");
        var document = await _client.GetJsonObjectAsync(url + "?write=true");
        document["versions"]!["1.0.0"]!["deprecated"] = "Old";
        using (var deprecate = await _client.PutJsonAsync(url, document))
        {
            Assert.Equal(HttpStatusCode.OK, deprecate.StatusCode);
        }

        document["versions"]!.AsObject().Remove("1.0.0");
        using var unpublishOne = await _client.PutJsonAsync($"{url}/-rev/1", document);
        using var unpublishAll = await _client.DeleteAsync($"{url}/-rev/1");

        Assert.Equal(HttpStatusCode.Forbidden, unpublishOne.StatusCode);
        Assert.Contains("unpublish", await unpublishOne.ReadNpmErrorAsync());
        Assert.Equal(HttpStatusCode.Forbidden, unpublishAll.StatusCode);
    }

    [Fact]
    public async Task DeletePermissionUnpublishes()
    {
        var feed = await _app.CreateNpmFeedAsync(Feed);
        await SignInWithTokenAsync(feed, canPull: true, canPush: true, canDelete: true);
        using (await _client.PublishAsync(Feed, "my-package", "1.0.0"))
        {
        }

        using var unpublish = await _client.DeleteAsync($"feeds/{Feed}/npm/my-package/-rev/1");
        using var read = await _client.GetAsync($"feeds/{Feed}/npm/my-package");

        Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    public async Task SignedInUserGetsNotFoundForTheOtherEcosystems()
    {
        var feed = await _app.CreateNpmFeedAsync(Feed);
        await SignInWithTokenAsync(feed, canPull: true, canPush: true, canDelete: false);

        using var nugetOnNpm = await _client.GetAsync($"feeds/{Feed}/v3/index.json");
        using var pubOnNpm = await _client.GetAsync($"feeds/{Feed}/api/packages/my_package");

        Assert.Equal(HttpStatusCode.NotFound, nugetOnNpm.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, pubOnNpm.StatusCode);
    }

    private async Task PublishAsAnotherUserAsync(Feed feed)
    {
        using var publisher = _app.CreateClient();
        publisher.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await CreateTokenAsync(feed, "publisher", canPull: true, canPush: true, canDelete: false));

        using var response = await publisher.PublishAsync(Feed, "my-package", "1.0.0");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// Creates a local account with the given permissions on the feed, and sends its personal
    /// access token as a bearer token from now on, like npm with an _authToken.
    /// </summary>
    private async Task SignInWithTokenAsync(Feed feed, bool canPull, bool canPush, bool canDelete)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await CreateTokenAsync(feed, "npmuser", canPull, canPush, canDelete));
    }

    private async Task<string> CreateTokenAsync(Feed feed, string username, bool canPull, bool canPush, bool canDelete)
    {
        using var scope = _app.Services.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var user = await userService.CreateLocalUserAsync(
            username, username, null, "TestPassword123!", canLoginToUI: false, createdByUserId: null, CancellationToken.None);

        if (canPull || canPush || canDelete)
        {
            await permissionService.GrantPermissionAsync(
                user.Id, PrincipalType.User, feed.Id, canPush: canPush, canPull: canPull, CancellationToken.None, canDelete: canDelete);
        }

        var token = await tokenService.CreateTokenAsync(user.Id, "npm", DateTime.UtcNow.AddDays(1), CancellationToken.None);
        return token.PlaintextToken;
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
