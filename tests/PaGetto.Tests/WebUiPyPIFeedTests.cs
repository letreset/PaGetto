using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.PyPI;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// PyPI feeds in the web UI with user accounts: creating one, showing and managing its packages.
/// </summary>
public class WebUiPyPIFeedTests : IDisposable
{
    private const string Password = "LocalPassword123!";

    private readonly PaGettoApplication _app;

    public WebUiPyPIFeedTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
    }

    [Fact]
    public async Task AdminCreatesAPyPIFeed()
    {
        await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
        using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);

        using var response = await admin.PostFormAsync("/Admin/Feeds", "Create", new Dictionary<string, string>
        {
            { "NewSlug", "python" },
            { "NewName", "Python packages" },
            { "NewType", nameof(FeedType.PyPI) },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(FeedType.PyPI, (await GetFeedAsync("python")).Type);

        // The list offers the index URL of the feed.
        Assert.Contains("data-copy=\"http://localhost/feeds/python/pypi/simple/\"", await response.Content.ReadAsStringAsync());

        var settings = await admin.GetStringAsync("/Admin/Feeds/python/Settings");
        Assert.Contains("https://pypi.org/simple", settings);
        Assert.Contains("Applies to each file of a version.", settings);

        var connect = await admin.GetStringAsync("/feeds/python/connect");
        Assert.Contains("http://localhost/feeds/python/pypi/simple/", connect);
        Assert.Contains("http://localhost/feeds/python/pypi/legacy/", connect);
        Assert.Contains("__token__", connect);
    }

    [Fact]
    public async Task ThePackagePageShowsTheProjectAndItsFiles()
    {
        var feed = await CreateFeedAsync("python");
        await UploadAsync(feed, "My_Package", "1.0", PyPITestClient.WheelFileName("My_Package", "1.0"), PyPITestClient.Wheel("My_Package", "1.0"));

        var user = await WebUiSession.SeedLocalUserAsync(_app, "reader", Password);
        await GrantAsync(user.Id, feed.Id, canDelete: false);
        using var reader = await WebUiSession.SignInAsync(_app, "reader", Password);

        var list = await reader.GetStringAsync("/feeds/python/");
        Assert.Contains("My_Package", list);
        Assert.Contains("pip install My_Package==1.0", list);

        // Any spelling of the name finds the project.
        var page = await reader.GetStringAsync("/feeds/python/artifacts/my.package");
        Assert.Contains("<h1>My_Package</h1>", page);
        Assert.Contains("pip install My_Package==1.0", page);
        Assert.Contains("my_package-1.0-py3-none-any.whl", page);
        Assert.DoesNotContain(".whl.metadata", page);
        Assert.Contains("http://localhost/feeds/python/pypi/files/my-package/1.0/my_package-1.0-py3-none-any.whl", page);
        Assert.Contains("Python &gt;=3.9", page);
        Assert.Contains("requests", page);
        Assert.Contains("The readme of My_Package.", page);
    }

    [Fact]
    public async Task UserWithDeletePermissionYanksAndDeletesAVersion()
    {
        var feed = await CreateFeedAsync("python");
        await UploadAsync(feed, "my-package", "1.0", PyPITestClient.SdistFileName("my-package", "1.0"), PyPITestClient.Sdist("my-package", "1.0"));
        await UploadAsync(feed, "my-package", "2.0", PyPITestClient.WheelFileName("my-package", "2.0"), PyPITestClient.Wheel("my-package", "2.0"));

        var user = await WebUiSession.SeedLocalUserAsync(_app, "manager", Password);
        await GrantAsync(user.Id, feed.Id, canDelete: true);
        using var manager = await WebUiSession.SignInAsync(_app, "manager", Password);

        const string page = "/feeds/python/artifacts/my-package/2.0";
        Assert.Contains("Yank 2.0", await manager.GetStringAsync(page));

        using (var yank = await manager.PostFormAsync("/feeds/python/artifacts/my-package/1.0", "Unlist", new Dictionary<string, string>()))
        {
            Assert.Equal(HttpStatusCode.Redirect, yank.StatusCode);
        }

        using (var delete = await manager.PostFormAsync(page, "Delete", new Dictionary<string, string>()))
        {
            Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        }

        using var scope = _app.Services.CreateScope();
        var artifacts = scope.ServiceProvider.GetRequiredService<IArtifactDatabase>();
        var storage = scope.ServiceProvider.GetRequiredService<IArtifactStorageService>();

        Assert.Null(await artifacts.FindVersionOrNullAsync(feed.Id, "my-package", "2.0", CancellationToken.None));
        Assert.False((await artifacts.FindVersionOrNullAsync(feed.Id, "my-package", "1.0", CancellationToken.None)).Listed);
        Assert.False(await IsStoredAsync(storage, feed, "2.0", PyPITestClient.WheelFileName("my-package", "2.0")));
        Assert.False(await IsStoredAsync(storage, feed, "2.0", PyPITestClient.WheelFileName("my-package", "2.0") + ".metadata"));
        Assert.True(await IsStoredAsync(storage, feed, "1.0", PyPITestClient.SdistFileName("my-package", "1.0")));
    }

    private static async Task<bool> IsStoredAsync(IArtifactStorageService storage, Feed feed, string version, string fileName)
    {
        try
        {
            await using var stream = await storage.GetFileStreamAsync(feed.Slug, "my-package", version, fileName, CancellationToken.None);
            return stream != null;
        }
        catch (IOException)
        {
            // The file system storage throws for a file that isn't there.
            return false;
        }
    }

    private async Task<Feed> CreateFeedAsync(string slug)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IFeedService>()
            .CreateFeedAsync(new Feed { Slug = slug, Name = slug, Type = FeedType.PyPI }, CancellationToken.None);
    }

    private async Task<Feed> GetFeedAsync(string slug)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IFeedService>().GetFeedBySlugAsync(slug, CancellationToken.None);
    }

    private async Task UploadAsync(Feed feed, string name, string version, string fileName, byte[] content)
    {
        using var scope = _app.Services.CreateScope();
        var packages = scope.ServiceProvider.GetRequiredService<IPyPIPackageService>();

        using var stream = new MemoryStream(content);
        var result = await packages.UploadAsync(
            feed,
            new PyPIUpload { Name = name, Version = version, FileName = fileName, Content = stream },
            maxBytes: 1024 * 1024,
            CancellationToken.None);

        Assert.Equal(PyPIUploadStatus.Success, result.Status);
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
