using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Maven;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Maven feeds in the web UI with user accounts: creating one, and managing its packages.
/// </summary>
public class WebUiMavenFeedTests : IDisposable
{
    private const string Password = "LocalPassword123!";

    private readonly PaGettoApplication _app;

    public WebUiMavenFeedTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
    }

    [Fact]
    public async Task AdminCreatesAMavenFeed()
    {
        await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
        using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);

        using var response = await admin.PostFormAsync("/Admin/Feeds", "Create", new Dictionary<string, string>
        {
            { "NewSlug", "jvm" },
            { "NewName", "JVM libraries" },
            { "NewType", nameof(FeedType.Maven) },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(FeedType.Maven, (await GetFeedAsync("jvm")).Type);

        // The list offers the repository URL of the feed.
        Assert.Contains("data-copy=\"http://localhost/feeds/jvm/maven2/\"", await response.Content.ReadAsStringAsync());

        var settings = await admin.GetStringAsync("/Admin/Feeds/jvm/Settings");
        Assert.Contains("https://repo.maven.apache.org/maven2", settings);
        Assert.Contains("Files of SNAPSHOT versions are always replaced.", settings);
        Assert.DoesNotContain("id=\"retention\"", settings);
    }

    [Fact]
    public async Task UserWithDeletePermissionDeletesAVersionAndItsFiles()
    {
        var feed = await CreateFeedAsync("jvm");
        await DeployAsync(feed, "1.0");
        await DeployAsync(feed, "2.0");

        var user = await WebUiSession.SeedLocalUserAsync(_app, "manager", Password);
        await GrantAsync(user.Id, feed.Id);
        using var manager = await WebUiSession.SignInAsync(_app, "manager", Password);

        const string page = "/feeds/jvm/artifacts/com.example:my-lib/2.0";
        var html = await manager.GetStringAsync(page);
        Assert.Contains("my-lib-2.0.jar", html);
        Assert.Contains("Delete 2.0", html);

        using (var delete = await manager.PostFormAsync(page, "Delete", new Dictionary<string, string>()))
        {
            Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        }

        using var scope = _app.Services.CreateScope();
        var artifacts = scope.ServiceProvider.GetRequiredService<IArtifactDatabase>();
        var storage = scope.ServiceProvider.GetRequiredService<IArtifactStorageService>();

        Assert.Null(await artifacts.FindVersionOrNullAsync(feed.Id, "com.example:my-lib", "2.0", CancellationToken.None));
        Assert.NotNull(await artifacts.FindVersionOrNullAsync(feed.Id, "com.example:my-lib", "1.0", CancellationToken.None));
        Assert.False(await IsStoredAsync(storage, feed, "2.0", "my-lib-2.0.jar"));
        Assert.False(await IsStoredAsync(storage, feed, "2.0", "my-lib-2.0.pom"));
        Assert.True(await IsStoredAsync(storage, feed, "1.0", "my-lib-1.0.jar"));
    }

    private static async Task<bool> IsStoredAsync(IArtifactStorageService storage, Feed feed, string version, string fileName)
    {
        try
        {
            await using var stream = await storage.GetFileStreamAsync(feed.Slug, "com.example:my-lib", version, fileName, CancellationToken.None);
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
            .CreateFeedAsync(new Feed { Slug = slug, Name = slug, Type = FeedType.Maven }, CancellationToken.None);
    }

    private async Task<Feed> GetFeedAsync(string slug)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IFeedService>().GetFeedBySlugAsync(slug, CancellationToken.None);
    }

    private async Task DeployAsync(Feed feed, string version)
    {
        using var scope = _app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMavenRepositoryService>();

        foreach (var (fileName, content) in new[]
        {
            ($"my-lib-{version}.pom", Encoding.UTF8.GetBytes(MavenTestClient.Pom("com.example", "my-lib", version))),
            ($"my-lib-{version}.jar", MavenTestClient.Jar(version)),
        })
        {
            Assert.True(MavenPath.TryParse($"com/example/my-lib/{version}/{fileName}", out var path));

            using var stream = new MemoryStream(content);
            var result = await repository.PutAsync(feed, path, stream, maxBytes: 1024 * 1024, CancellationToken.None);
            Assert.Equal(MavenPutStatus.Success, result.Status);
        }
    }

    private async Task GrantAsync(Guid userId, Guid feedId)
    {
        using var scope = _app.Services.CreateScope();
        var permissions = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        await permissions.GrantPermissionAsync(
            userId, PrincipalType.User, feedId, canPush: false, canPull: true, CancellationToken.None, canDelete: true);
    }

    public void Dispose()
    {
        _app.Dispose();
    }
}
