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
using PaGetto.Core.VsCode;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The gallery of a VS Code feed with user accounts. The editors send no credentials to a gallery,
/// so they read it through the URL with a personal access token in its path.
/// </summary>
public class VsCodeAuthenticationIntegrationTests : IDisposable
{
    private const string Feed = "code";
    private const string Id = "Contoso.hello-world";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public VsCodeAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task AnonymousRequestIsChallengedForCredentials()
    {
        await _app.CreateVsCodeFeedAsync(Feed);

        using var query = await _client.PostVsCodeQueryAsync($"feeds/{Feed}/vscode/gallery", []);
        using var asset = await _client.GetAsync($"feeds/{Feed}/vscode/asset/Contoso/hello-world/1.0.0/{VsCodeGalleryApi.VsixPackageAsset}");

        Assert.Equal(HttpStatusCode.Unauthorized, query.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asset.StatusCode);
        Assert.Equal("Basic realm=\"PaGetto\"", query.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task EditorReadsTheGalleryThroughTheUrlWithAToken()
    {
        var feed = await _app.CreateVsCodeFeedAsync(Feed);
        _client.UseBasicCredentials("__token__", await CreateUserAsync(feed, "publisher", canPull: true, canPush: true));
        var vsix = VsCodePackageBuilder.Create("Contoso", "hello-world", "1.0.0");
        using (var upload = await _client.UploadVsCodeAsync(Feed, vsix))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        var token = await CreateUserAsync(feed, "reader", canPull: true);
        _client.DefaultRequestHeaders.Authorization = null;

        var extension = await _client.FindVsCodeExtensionAsync(Feed, Id, token);
        var version = Assert.Single(extension.Versions);

        // The URLs of the answer carry the token, so the files are read the same way.
        Assert.Equal($"http://localhost/feeds/{Feed}/vscode/auth/{token}/asset/Contoso/hello-world/1.0.0", version.AssetUri);
        Assert.Equal(vsix, await _client.GetByteArrayAsync(version.GetAssetUrl(VsCodeGalleryApi.VsixPackageAsset)));

        using var latest = await _client.GetAsync($"feeds/{Feed}/vscode/auth/{token}/gallery/vscode/Contoso/hello-world/latest");
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
    }

    [Theory]
    [InlineData("PUT", "upload")]
    [InlineData("POST", "upload")]
    [InlineData("POST", "gallery/vscode/Contoso/hello-world/latest")]
    public async Task TheUrlWithATokenOnlyReadsAndQueries(string method, string path)
    {
        var feed = await _app.CreateVsCodeFeedAsync(Feed);
        var token = await CreateUserAsync(feed, "publisher", canPull: true, canPush: true);

        using var request = new HttpRequestMessage(new HttpMethod(method), $"feeds/{Feed}/vscode/auth/{token}/{path}")
        {
            Content = new ByteArrayContent(VsCodePackageBuilder.Create("Contoso", "hello-world", "1.0.0")),
        };
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheUrlWithATokenOfAUserWithoutPullPermissionIsNotFound()
    {
        var feed = await _app.CreateVsCodeFeedAsync(Feed);
        var token = await CreateUserAsync(feed, "stranger", canPull: false);

        using var response = await _client.PostVsCodeQueryAsync($"feeds/{Feed}/vscode/auth/{token}/gallery", []);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PullOnlyUserCannotUpload()
    {
        var feed = await _app.CreateVsCodeFeedAsync(Feed);
        _client.UseBasicCredentials("reader", await CreateUserAsync(feed, "reader", canPull: true));

        using var upload = await _client.UploadVsCodeAsync(Feed, VsCodePackageBuilder.Create("Contoso", "hello-world", "1.0.0"));

        Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
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

        var token = await tokenService.CreateTokenAsync(user.Id, "code", DateTime.UtcNow.AddDays(1), CancellationToken.None);
        return token.PlaintextToken;
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
