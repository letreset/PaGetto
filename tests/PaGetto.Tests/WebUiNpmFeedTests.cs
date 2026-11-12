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
using PaGetto.Core.Npm;
using PaGetto.Core.Tests.Support;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// npm feeds in the web UI with user accounts: creating one, its pages, and managing its packages.
/// </summary>
public class WebUiNpmFeedTests : IDisposable
{
    private const string Password = "LocalPassword123!";

    private readonly PaGettoApplication _app;

    public WebUiNpmFeedTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
    }

    [Fact]
    public async Task AdminCreatesAnNpmFeed()
    {
        await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
        using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);

        using var response = await admin.PostFormAsync("/Admin/Feeds", "Create", new Dictionary<string, string>
        {
            { "NewSlug", "js" },
            { "NewName", "JavaScript packages" },
            { "NewType", nameof(FeedType.Npm) },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(FeedType.Npm, (await GetFeedAsync("js")).Type);

        // The list offers the registry URL of the feed.
        Assert.Contains("data-copy=\"http://localhost/feeds/js/npm/\"", await response.Content.ReadAsStringAsync());

        var settings = await admin.GetStringAsync("/Admin/Feeds/js/Settings");
        Assert.Contains("https://registry.npmjs.org", settings);
        Assert.DoesNotContain("id=\"retention\"", settings);
    }

    [Fact]
    public async Task ConnectPageShowsTheRegistrySetup()
    {
        var feed = await _app.CreateNpmFeedAsync("js");
        var user = await WebUiSession.SeedLocalUserAsync(_app, "dev", Password);
        await GrantAsync(user.Id, feed.Id, canDelete: false);
        using var dev = await WebUiSession.SignInAsync(_app, "dev", Password);

        var connect = await dev.GetStringAsync("/feeds/js/Connect");

        Assert.Contains("http://localhost/feeds/js/npm/", connect);
        Assert.Contains("//localhost/feeds/js/npm/:_authToken=YOUR_TOKEN", connect);
        Assert.Contains("npmRegistryServer", connect);
    }

    [Fact]
    public async Task PackagePageOfAScopedPackage()
    {
        var feed = await _app.CreateNpmFeedAsync("js");
        await PublishAsync(feed, "@acme/ui-kit", "1.0.0");
        await PublishAsync(feed, "@acme/ui-kit", "1.1.0");

        var user = await WebUiSession.SeedLocalUserAsync(_app, "dev", Password);
        await GrantAsync(user.Id, feed.Id, canDelete: false);
        using var dev = await WebUiSession.SignInAsync(_app, "dev", Password);

        var list = await dev.GetStringAsync("/feeds/js/");
        Assert.Contains("@acme/ui-kit", list);
        Assert.Contains("npm install @acme/ui-kit@1.1.0", list);

        // The link of the list encodes the slash of the name.
        Assert.Contains("/feeds/js/artifacts/@acme%2Fui-kit/1.1.0", list);

        var page = await dev.GetStringAsync("/feeds/js/artifacts/%40acme%2Fui-kit");
        Assert.Contains("@acme/ui-kit", page);
        Assert.Contains("yarn add @acme/ui-kit@1.1.0", page);
        Assert.Contains("/feeds/js/npm/@acme/ui-kit/-/ui-kit-1.1.0.tgz", page);
        Assert.Contains("Download tarball", page);

        // The links of the Versions tab encode the name once.
        Assert.Contains("href=\"/feeds/js/artifacts/@acme%2Fui-kit/1.0.0\"", page);
        Assert.DoesNotContain("%252", page);
    }

    [Fact]
    public async Task LatestIsTheVersionOfTheLatestTagEvenWhenDeprecated()
    {
        var feed = await _app.CreateNpmFeedAsync("js");
        await PublishAsync(feed, "my-package", "1.0.0");
        await PublishAsync(feed, "my-package", "2.0.0-beta.1", tag: "beta");

        var user = await WebUiSession.SeedLocalUserAsync(_app, "manager", Password);
        await GrantAsync(user.Id, feed.Id, canDelete: true);
        using var manager = await WebUiSession.SignInAsync(_app, "manager", Password);

        using (await manager.PostFormAsync("/feeds/js/artifacts/my-package/1.0.0", "Unlist", new Dictionary<string, string>()))
        {
        }

        // npm still installs 1.0.0 by default, so the web UI shows it too.
        Assert.Contains("v1.0.0", await manager.GetStringAsync("/feeds/js/"));

        var page = await manager.GetStringAsync("/feeds/js/artifacts/my-package");
        Assert.Contains("This version is deprecated", page);
        Assert.Contains("npm install my-package@1.0.0", page);
    }

    [Fact]
    public async Task UserWithDeletePermissionDeprecatesUndeprecatesAndDeletesAVersion()
    {
        var feed = await _app.CreateNpmFeedAsync("js");
        await PublishAsync(feed, "my-package", "1.0.0");
        await PublishAsync(feed, "my-package", "2.0.0");

        var user = await WebUiSession.SeedLocalUserAsync(_app, "manager", Password);
        await GrantAsync(user.Id, feed.Id, canDelete: true);
        using var manager = await WebUiSession.SignInAsync(_app, "manager", Password);

        const string page = "/feeds/js/artifacts/my-package/2.0.0";
        Assert.Contains("Deprecating keeps the file", await manager.GetStringAsync(page));

        using (var deprecate = await manager.PostFormAsync(page, "Unlist", new Dictionary<string, string>()))
        {
            Assert.Equal(HttpStatusCode.Redirect, deprecate.StatusCode);
        }

        Assert.False((await FindVersionAsync(feed, "my-package", "2.0.0")).Listed);
        Assert.Contains("This version is deprecated", await manager.GetStringAsync(page));

        using (var undeprecate = await manager.PostFormAsync(page, "Relist", new Dictionary<string, string>()))
        {
            Assert.Equal(HttpStatusCode.Redirect, undeprecate.StatusCode);
        }

        Assert.True((await FindVersionAsync(feed, "my-package", "2.0.0")).Listed);

        using (var delete = await manager.PostFormAsync(page, "Delete", new Dictionary<string, string>()))
        {
            Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        }

        Assert.Null(await FindVersionAsync(feed, "my-package", "2.0.0"));
        Assert.NotNull(await FindVersionAsync(feed, "my-package", "1.0.0"));
    }

    private async Task<Feed> GetFeedAsync(string slug)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IFeedService>().GetFeedBySlugAsync(slug, CancellationToken.None);
    }

    private async Task PublishAsync(Feed feed, string name, string version, string tag = "latest")
    {
        using var scope = _app.Services.CreateScope();
        var body = NpmTestClient.CreatePublishBody(name, version, tag: tag);

        using var document = System.Text.Json.JsonDocument.Parse(body.ToJsonString());
        var result = await scope.ServiceProvider.GetRequiredService<INpmPackageService>()
            .PublishAsync(feed, name, document.RootElement, maxTarballBytes: 1024 * 1024, CancellationToken.None);
        Assert.Equal(NpmStatus.Success, result.Status);
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
