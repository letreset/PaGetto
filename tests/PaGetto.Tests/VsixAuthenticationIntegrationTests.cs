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
/// The gallery of a VSIX feed with user accounts: Visual Studio answers the Basic challenge with
/// the credentials it asks for, a username and a personal access token as the password.
/// </summary>
public class VsixAuthenticationIntegrationTests : IDisposable
{
    private const string Feed = "vs";
    private const string Id = "Contoso.HelloTools";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public VsixAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("feeds/vs/vsix/atom.xml")]
    [InlineData("feeds/vs/vsix/extensions/Contoso.HelloTools/1.0/Contoso.HelloTools-1.0.vsix")]
    [InlineData("feeds/vs/vsix/extensions/Contoso.HelloTools/1.0/icon")]
    // Feeds that don't exist answer the same, so feeds can't be probed.
    [InlineData("feeds/missing/vsix/atom.xml")]
    public async Task AnonymousRequestIsChallengedForCredentials(string path)
    {
        await _app.CreateVsixFeedAsync(Feed);

        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic realm=\"PaGetto\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Theory]
    [InlineData("__token__")]
    [InlineData("publisher")]
    public async Task UserWithPushPermissionUploadsAndInstallsWithAToken(string username)
    {
        var feed = await _app.CreateVsixFeedAsync(Feed);
        var token = await CreateUserAsync(feed, "publisher", canPull: true, canPush: true);
        _client.UseBasicCredentials(username, token);

        using var upload = await _client.UploadVsixAsync(Feed, Id, "1.0");
        using var gallery = await _client.GetAsync($"feeds/{Feed}/vsix/atom.xml");
        using var vsix = await _client.GetAsync($"feeds/{Feed}/vsix/extensions/{Id}/1.0/{Id}-1.0.vsix");

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.Equal(HttpStatusCode.OK, gallery.StatusCode);
        Assert.Equal(HttpStatusCode.OK, vsix.StatusCode);
    }

    [Fact]
    public async Task PullOnlyUserCanInstallButNotUpload()
    {
        var feed = await _app.CreateVsixFeedAsync(Feed);
        _client.UseBasicCredentials("__token__", await CreateUserAsync(feed, "publisher", canPull: true, canPush: true));
        using (await _client.UploadVsixAsync(Feed, Id, "1.0"))
        {
        }

        _client.UseBasicCredentials("reader", await CreateUserAsync(feed, "reader", canPull: true));
        using var vsix = await _client.GetAsync($"feeds/{Feed}/vsix/extensions/{Id}/1.0/{Id}-1.0.vsix");
        using var upload = await _client.UploadVsixAsync(Feed, Id, "2.0");

        Assert.Equal(HttpStatusCode.OK, vsix.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
        Assert.Contains("permission", await upload.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TokenOfAUserWithoutPullPermissionIsNotFound()
    {
        var feed = await _app.CreateVsixFeedAsync(Feed);
        _client.UseBasicCredentials("__token__", await CreateUserAsync(feed, "stranger", canPull: false));

        using var response = await _client.GetAsync($"feeds/{Feed}/vsix/atom.xml");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task VisualStudioReadsTheGalleryThroughTheUrlWithAToken()
    {
        var feed = await _app.CreateVsixFeedAsync(Feed);
        _client.UseBasicCredentials("__token__", await CreateUserAsync(feed, "publisher", canPull: true, canPush: true));
        var vsix = Core.Tests.Support.VsixPackageBuilder.Create(Id, "1.0");
        using (await _client.UploadVsixAsync(Feed, Id, "1.0", vsix))
        {
        }

        // Visual Studio sends no Authorization header.
        var token = await CreateUserAsync(feed, "reader", canPull: true);
        _client.DefaultRequestHeaders.Authorization = null;

        var entry = (await _client.GetVsixGalleryAsync(Feed, token))[Id];

        // The gallery's links carry the token, so the file and images are read the same way.
        Assert.Equal($"http://localhost/feeds/{Feed}/vsix/auth/{token}/extensions/{Id}/1.0/{Id}-1.0.vsix", entry.DownloadUrl);
        Assert.Equal($"http://localhost/feeds/{Feed}/vsix/auth/{token}/extensions/{Id}/1.0/icon", entry.IconUrl);
        Assert.Equal(vsix, await _client.GetByteArrayAsync(entry.DownloadUrl));
        Assert.Equal(Core.Tests.Support.VsixPackageBuilder.Icon, await _client.GetByteArrayAsync(entry.IconUrl));
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("POST")]
    public async Task TheUrlWithATokenCannotUpload(string method)
    {
        var feed = await _app.CreateVsixFeedAsync(Feed);
        var token = await CreateUserAsync(feed, "publisher", canPull: true, canPush: true);

        using var request = new HttpRequestMessage(new HttpMethod(method), $"feeds/{Feed}/vsix/auth/{token}/upload")
        {
            Content = new ByteArrayContent(Core.Tests.Support.VsixPackageBuilder.Create(Id, "1.0")),
        };
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _client.UseBasicCredentials("__token__", token);
        Assert.Empty(await _client.GetVsixGalleryAsync(Feed));
    }

    [Fact]
    public async Task TheUrlWithAnInvalidTokenIsChallenged()
    {
        await _app.CreateVsixFeedAsync(Feed);

        using var response = await _client.GetAsync($"feeds/{Feed}/vsix/auth/bg_not-a-real-token/atom.xml");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TheUrlWithATokenOfAUserWithoutPullPermissionIsNotFound()
    {
        var feed = await _app.CreateVsixFeedAsync(Feed);
        var token = await CreateUserAsync(feed, "stranger", canPull: false);

        using var response = await _client.GetAsync($"feeds/{Feed}/vsix/auth/{token}/atom.xml");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheUrlWithATokenIsNotFoundOnOtherFeeds()
    {
        var feed = await _app.CreateCranFeedAsync("r");
        var token = await CreateUserAsync(feed, "reader", canPull: true);

        using var response = await _client.GetAsync($"feeds/r/vsix/auth/{token}/atom.xml");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

        var token = await tokenService.CreateTokenAsync(user.Id, "vs", DateTime.UtcNow.AddDays(1), CancellationToken.None);
        return token.PlaintextToken;
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
