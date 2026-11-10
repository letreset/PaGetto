using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Pub;
using PaGetto.Core.Tests.Support;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Dart feeds in the web UI with user accounts: creating one, and managing its packages.
/// </summary>
public class WebUiDartFeedTests : IDisposable
{
    private const string Password = "LocalPassword123!";

    private readonly PaGettoApplication _app;

    public WebUiDartFeedTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
    }

    [Fact]
    public async Task AdminCreatesADartFeed()
    {
        await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
        using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);

        using var response = await admin.PostFormAsync("/Admin/Feeds", "Create", new Dictionary<string, string>
        {
            { "NewSlug", "dart" },
            { "NewName", "Dart packages" },
            { "NewType", nameof(FeedType.Pub) },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var feed = await GetFeedAsync("dart");
        Assert.Equal(FeedType.Pub, feed.Type);

        // The list offers the hosted URL of the feed, not a NuGet service index.
        var list = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-copy=\"http://localhost/feeds/dart\"", list);
        Assert.Contains("data-copy=\"http://localhost/feeds/default/v3/index.json\"", list);

        // Settings that only NuGet feeds have are left out.
        var dartSettings = await admin.GetStringAsync("/Admin/Feeds/dart/Settings");
        var nugetSettings = await admin.GetStringAsync("/Admin/Feeds/default/Settings");
        Assert.DoesNotContain("id=\"retention\"", dartSettings);
        Assert.Contains("https://pub.dev", dartSettings);
        Assert.Contains("id=\"retention\"", nugetSettings);
    }

    [Fact]
    public async Task ExistingFeedsAreNuGetFeeds()
    {
        Assert.Equal(FeedType.NuGet, (await GetFeedAsync(Feed.DefaultSlug)).Type);
    }

    [Fact]
    public async Task UserWithDeletePermissionRetractsRestoresAndDeletesAVersion()
    {
        var feed = await _app.CreatePubFeedAsync("dart");
        await PublishAsync(feed, "my_package", "1.0.0");
        await PublishAsync(feed, "my_package", "2.0.0");

        var user = await WebUiSession.SeedLocalUserAsync(_app, "manager", Password);
        await GrantAsync(user.Id, feed.Id, canDelete: true);
        using var manager = await WebUiSession.SignInAsync(_app, "manager", Password);

        const string page = "/feeds/dart/artifacts/my_package/2.0.0";

        using (var retract = await manager.PostFormAsync(page, "Unlist", new Dictionary<string, string>()))
        {
            Assert.Equal(HttpStatusCode.Redirect, retract.StatusCode);
        }

        Assert.False((await FindVersionAsync(feed, "my_package", "2.0.0")).Listed);
        Assert.Contains("retracted", await manager.GetStringAsync(page));

        using (var restore = await manager.PostFormAsync(page, "Relist", new Dictionary<string, string>()))
        {
            Assert.Equal(HttpStatusCode.Redirect, restore.StatusCode);
        }

        Assert.True((await FindVersionAsync(feed, "my_package", "2.0.0")).Listed);

        using (var delete = await manager.PostFormAsync(page, "Delete", new Dictionary<string, string>()))
        {
            Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        }

        Assert.Null(await FindVersionAsync(feed, "my_package", "2.0.0"));
        Assert.NotNull(await FindVersionAsync(feed, "my_package", "1.0.0"));
    }

    [Fact]
    public async Task UserWithoutDeletePermissionCannotManageVersions()
    {
        var feed = await _app.CreatePubFeedAsync("dart");
        await PublishAsync(feed, "my_package", "1.0.0");

        var user = await WebUiSession.SeedLocalUserAsync(_app, "reader", Password);
        await GrantAsync(user.Id, feed.Id, canDelete: false);
        using var reader = await WebUiSession.SignInAsync(_app, "reader", Password);

        const string page = "/feeds/dart/artifacts/my_package/1.0.0";

        Assert.DoesNotContain("handler=Delete", await reader.GetStringAsync(page));

        using var delete = await reader.PostFormAsync(page, "Delete", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.NotNull(await FindVersionAsync(feed, "my_package", "1.0.0"));
    }

    [Fact]
    public async Task UserWithoutAccessDoesNotSeeTheFeed()
    {
        var feed = await _app.CreatePubFeedAsync("dart");
        await PublishAsync(feed, "my_package", "1.0.0");

        // The account can only pull from the default feed.
        await WebUiSession.SeedLocalUserAsync(_app, "outsider", Password);
        using var outsider = await WebUiSession.SignInAsync(_app, "outsider", Password);

        using var index = await outsider.Client.GetAsync("/feeds/dart/");
        using var details = await outsider.Client.GetAsync("/feeds/dart/artifacts/my_package");

        Assert.Equal(HttpStatusCode.NotFound, index.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);
    }

    private async Task<Feed> GetFeedAsync(string slug)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IFeedService>().GetFeedBySlugAsync(slug, CancellationToken.None);
    }

    private async Task PublishAsync(Feed feed, string name, string version)
    {
        using var scope = _app.Services.CreateScope();
        using var archive = PubArchiveBuilder.Create(name, version);

        var result = await scope.ServiceProvider.GetRequiredService<IPubPackageService>().PublishAsync(feed, archive, CancellationToken.None);
        Assert.Equal(PubPublishStatus.Success, result.Status);
    }

    private async Task<ArtifactVersion> FindVersionAsync(Feed feed, string name, string version)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IArtifactDatabase>().FindVersionOrNullAsync(feed.Id, name, version, CancellationToken.None);
    }

    private async Task GrantAsync(Guid userId, Guid feedId, bool canDelete)
    {
        using var scope = _app.Services.CreateScope();
        var permissions = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        await permissions.GrantPermissionAsync(
            userId, PrincipalType.User, feedId, canPush: false, canPull: true, CancellationToken.None, canDelete: canDelete);
    }

    public void Dispose()
    {
        _app.Dispose();
    }
}
