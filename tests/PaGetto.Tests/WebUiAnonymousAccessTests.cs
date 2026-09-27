using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// In the Local, Entra and Hybrid modes an anonymous visitor only gets a sign-in prompt:
/// no feed data is loaded, no upstream is called, and nothing about the feed is disclosed.
/// </summary>
public class WebUiAnonymousAccessTests : IDisposable
{
    private readonly PaGettoApplication _upstream;
    private readonly CountingHandler _upstreamCalls;
    private readonly PaGettoApplication _app;

    public WebUiAnonymousAccessTests(ITestOutputHelper output)
    {
        _upstream = new PaGettoApplication(output);
        _upstreamCalls = new CountingHandler(_upstream.Server.CreateHandler());
        _app = new PaGettoApplication(output, _upstreamCalls, dict =>
        {
            dict["Authentication:Mode"] = "Local";
            dict["Statistics:EnableStatisticsPage"] = "true";
        });
    }

    [Fact]
    public async Task PackagePageOfMirroredFeed_DoesNotLoadThePackageOrCallUpstreams()
    {
        await _upstream.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));
        using var client = _app.CreateClient();

        using var response = await client.GetAsync("/packages/TestData");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sign in required", body);
        Assert.Contains("<title>Sign in required - PaGetto</title>", body);
        Assert.DoesNotContain("TestData", body);
        Assert.DoesNotContain("1.2.3", body);
        Assert.Equal(0, _upstreamCalls.Count);
    }

    [Theory]
    [InlineData("/feeds/internal/")]
    [InlineData("/feeds/internal/packages/Contoso.Logging")]
    [InlineData("/feeds/internal/Connect")]
    [InlineData("/feeds/internal/Upload")]
    [InlineData("/feeds/internal/stats")]
    public async Task FeedPages_DoNotNameTheFeed(string path)
    {
        await _app.CreateFeedAsync("internal", "Internal Secret Feed");
        using var client = _app.CreateClient();

        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Sign in required - PaGetto</title>", body);
        Assert.DoesNotContain("Internal Secret Feed", body);
        Assert.DoesNotContain("Contoso.Logging", body);
    }

    public void Dispose()
    {
        _app.Dispose();
        _upstream.Dispose();
    }

    private sealed class CountingHandler : DelegatingHandler
    {
        private int _count;

        public CountingHandler(HttpMessageHandler inner) : base(inner)
        {
        }

        public int Count => _count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
