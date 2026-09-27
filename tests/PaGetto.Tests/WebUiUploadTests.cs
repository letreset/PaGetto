using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Browser uploads from the Upload page, through the real routing, antiforgery and indexing.
/// </summary>
public class WebUiUploadTests : IDisposable
{
    private const string Password = "LocalPassword123!";

    private readonly PaGettoApplication _app;

    public WebUiUploadTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
    }

    [Fact]
    public async Task UploadsAPackageAndItsSymbolsToAFeed()
    {
        using var session = await SignInWithPushToAsync("team");

        using var package = await session.PostFileAsync(
            "/feeds/team/Upload", "Package", TestResources.GetResourceStream(TestResources.Package), "TestData.1.2.3.nupkg");
        using var symbols = await session.PostFileAsync(
            "/feeds/team/Upload", "Symbol", TestResources.GetResourceStream(TestResources.SymbolPackage), "TestData.1.2.3.snupkg");

        Assert.Equal(HttpStatusCode.Created, package.StatusCode);
        Assert.Contains("/feeds/team/packages/", await package.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Created, symbols.StatusCode);

        var versions = await session.GetStringAsync("/feeds/team/v3/package/testdata/index.json");
        Assert.Contains("1.2.3", versions);
    }

    [Fact]
    public async Task ReadOnlyFeedRejectsTheUpload()
    {
        using var session = await SignInWithPushToAsync("team");
        await SetReadOnlyAsync("team");

        using var response = await session.PostFileAsync(
            "/feeds/team/Upload", "Package", TestResources.GetResourceStream(TestResources.Package), "TestData.1.2.3.nupkg");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UploadWithoutTheAntiforgeryTokenIsRejected()
    {
        using var session = await SignInWithPushToAsync("team");

        using var response = await session.PostFileAsync(
            "/feeds/team/Upload", "Package", TestResources.GetResourceStream(TestResources.Package), "TestData.1.2.3.nupkg",
            includeToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PullOnlyUserCantUpload()
    {
        await WebUiSession.SeedLocalUserAsync(_app, "reader", Password);
        using var session = await WebUiSession.SignInAsync(_app, "reader", Password);

        // The Upload page is a 404 for this user, so the token comes from the package list.
        using var response = await session.PostFileAsync(
            "/Upload", "Package", TestResources.GetResourceStream(TestResources.Package), "TestData.1.2.3.nupkg",
            tokenPagePath: "/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var versions = await session.Client.GetAsync("/v3/package/testdata/index.json");
        Assert.Equal(HttpStatusCode.NotFound, versions.StatusCode);
    }

    private async Task<WebUiSession> SignInWithPushToAsync(string slug)
    {
        var feed = await _app.CreateFeedAsync(slug);
        var user = await WebUiSession.SeedLocalUserAsync(_app, "uploader", Password);

        using (var scope = _app.Services.CreateScope())
        {
            var permissions = scope.ServiceProvider.GetRequiredService<IPermissionService>();
            await permissions.GrantPermissionAsync(
                user.Id, PrincipalType.User, feed.Id, canPush: true, canPull: true, CancellationToken.None);
        }

        return await WebUiSession.SignInAsync(_app, "uploader", Password);
    }

    private async Task SetReadOnlyAsync(string slug)
    {
        using var scope = _app.Services.CreateScope();
        var feeds = scope.ServiceProvider.GetRequiredService<IFeedService>();
        var feed = await feeds.GetFeedBySlugAsync(slug, CancellationToken.None);
        feed.IsReadOnlyMode = true;
        await feeds.UpdateFeedAsync(feed, CancellationToken.None);
    }

    public void Dispose()
    {
        _app.Dispose();
    }
}
