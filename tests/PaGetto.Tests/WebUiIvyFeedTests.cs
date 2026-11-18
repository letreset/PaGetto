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
using PaGetto.Core.Ivy;
using PaGetto.Core.Maven;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Ivy feeds in the web UI with user accounts: creating one, and managing its modules.
/// </summary>
public class WebUiIvyFeedTests : IDisposable
{
    private const string Password = "LocalPassword123!";
    private const string PackageName = "com.example:sbt-tool:scala_2.12:sbt_1.0";

    private readonly PaGettoApplication _app;

    public WebUiIvyFeedTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
    }

    [Fact]
    public async Task AdminCreatesAnIvyFeed()
    {
        await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
        using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);

        using var response = await admin.PostFormAsync("/Admin/Feeds", "Create", new Dictionary<string, string>
        {
            { "NewSlug", "sbt" },
            { "NewName", "sbt plugins" },
            { "NewType", nameof(FeedType.Ivy) },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(FeedType.Ivy, (await GetFeedAsync("sbt")).Type);

        // The list offers the repository URL of the feed.
        Assert.Contains("data-copy=\"http://localhost/feeds/sbt/ivy\"", await response.Content.ReadAsStringAsync());

        var settings = await admin.GetStringAsync("/Admin/Feeds/sbt/Settings");
        Assert.Contains("https://repo.scala-sbt.org/scalasbt/sbt-plugin-releases", settings);
        Assert.Contains("Files of SNAPSHOT versions are always replaced.", settings);
    }

    [Fact]
    public async Task UserWithDeletePermissionDeletesARevisionAndItsFiles()
    {
        var feed = await CreateFeedAsync("sbt");
        await PublishAsync(feed, "1.0.0");
        await PublishAsync(feed, "2.0.0");

        var user = await WebUiSession.SeedLocalUserAsync(_app, "manager", Password);
        await GrantAsync(user.Id, feed.Id);
        using var manager = await WebUiSession.SignInAsync(_app, "manager", Password);

        var page = $"/feeds/sbt/artifacts/{PackageName}/2.0.0";
        var html = await manager.GetStringAsync(page);
        Assert.Contains("jars/sbt-tool.jar", html);
        Assert.Contains("sbt 1.0", html);
        Assert.Contains("Scala 2.12", html);
        Assert.Contains("Delete 2.0.0", html);

        using (var delete = await manager.PostFormAsync(page, "Delete", new Dictionary<string, string>()))
        {
            Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        }

        using var scope = _app.Services.CreateScope();
        var artifacts = scope.ServiceProvider.GetRequiredService<IArtifactDatabase>();
        var storage = scope.ServiceProvider.GetRequiredService<IArtifactStorageService>();

        Assert.Null(await artifacts.FindVersionOrNullAsync(feed.Id, PackageName, "2.0.0", CancellationToken.None));
        Assert.NotNull(await artifacts.FindVersionOrNullAsync(feed.Id, PackageName, "1.0.0", CancellationToken.None));
        Assert.False(await IsStoredAsync(storage, feed, "2.0.0", "jars/sbt-tool.jar"));
        Assert.False(await IsStoredAsync(storage, feed, "2.0.0", IvyPath.DescriptorFileName));
        Assert.True(await IsStoredAsync(storage, feed, "1.0.0", "jars/sbt-tool.jar"));
    }

    private static async Task<bool> IsStoredAsync(IArtifactStorageService storage, Feed feed, string version, string fileName)
    {
        try
        {
            await using var stream = await storage.GetFileStreamAsync(feed.Slug, PackageName, version, fileName, CancellationToken.None);
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
            .CreateFeedAsync(new Feed { Slug = slug, Name = slug, Type = FeedType.Ivy }, CancellationToken.None);
    }

    private async Task<Feed> GetFeedAsync(string slug)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IFeedService>().GetFeedBySlugAsync(slug, CancellationToken.None);
    }

    private async Task PublishAsync(Feed feed, string revision)
    {
        using var scope = _app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IIvyRepositoryService>();

        foreach (var (fileName, content) in new[]
        {
            ("jars/sbt-tool.jar", MavenTestClient.Jar(revision)),
            (IvyPath.DescriptorFileName, Encoding.UTF8.GetBytes(IvyTestClient.PluginDescriptor("sbt-tool", revision))),
        })
        {
            Assert.True(IvyPath.TryParse(IvyPath.GetFilePath("com.example", "sbt-tool", "2.12", "1.0", revision, fileName), out var path));

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
