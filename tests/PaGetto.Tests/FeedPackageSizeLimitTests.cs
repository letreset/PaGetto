using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Feeds;
using PaGetto.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// A feed's max package size lowers the server-wide limit for pushes to that feed.
/// </summary>
public class FeedPackageSizeLimitTests : IDisposable
{
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public FeedPackageSizeLimitTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient();
    }

    [Fact]
    public async Task PushLargerThanFeedLimitIsRejected()
    {
        await _app.CreateFeedAsync("small");
        await SetMaxPackageSizeAsync("small", 0);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, await PushAsync("feeds/small/api/v2/package", TestResources.Package));
    }

    [Fact]
    public async Task PushWithinFeedLimitIsAccepted()
    {
        await _app.CreateFeedAsync("small");
        await SetMaxPackageSizeAsync("small", 1);

        Assert.Equal(HttpStatusCode.Created, await PushAsync("feeds/small/api/v2/package", TestResources.Package));
    }

    [Fact]
    public async Task SymbolPushLargerThanFeedLimitIsRejected()
    {
        await _app.CreateFeedAsync("small");
        Assert.Equal(HttpStatusCode.Created, await PushAsync("feeds/small/api/v2/package", TestResources.Package));
        await SetMaxPackageSizeAsync("small", 0);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, await PushAsync("feeds/small/api/v2/symbol", TestResources.SymbolPackage));
    }

    private async Task SetMaxPackageSizeAsync(string slug, uint gib)
    {
        using var scope = _app.Services.CreateScope();
        var feeds = scope.ServiceProvider.GetRequiredService<IFeedService>();
        var feed = await feeds.GetFeedBySlugAsync(slug, CancellationToken.None);
        feed.MaxPackageSizeGiB = gib;
        await feeds.UpdateFeedAsync(feed, CancellationToken.None);
    }

    private async Task<HttpStatusCode> PushAsync(string path, string resource)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(TestResources.GetResourceStream(resource)), "package", "package.nupkg");

        using var response = await _client.PutAsync(path, content);
        return response.StatusCode;
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
