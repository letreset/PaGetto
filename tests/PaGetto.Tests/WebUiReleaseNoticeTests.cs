using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Updates;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The notice on the admin pages that a newer PaGetto release exists, with the latest release
/// read from a fake GitHub.
/// </summary>
public class WebUiReleaseNoticeTests
{
    private const string Password = "LocalPassword123!";

    private readonly ITestOutputHelper _output;

    public WebUiReleaseNoticeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ShowsANewerRelease()
    {
        var page = await GetAdminPageAsync(new FakeGitHub("v99.0.0"), enabled: true);

        Assert.Contains("PaGetto <strong>99.0.0</strong> is available", page, StringComparison.Ordinal);
        Assert.Contains("href=\"https://github.com/letreset/PaGetto/releases/tag/v99.0.0\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShowsNothingWithoutANewerRelease()
    {
        var page = await GetAdminPageAsync(new FakeGitHub("v0.0.1"), enabled: true);

        Assert.DoesNotContain("is available", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShowsNothingWhenTurnedOff()
    {
        var gitHub = new FakeGitHub("v99.0.0");

        var page = await GetAdminPageAsync(gitHub, enabled: false);

        Assert.DoesNotContain("is available", page, StringComparison.Ordinal);
        Assert.Equal(0, gitHub.RequestCount);
    }

    [Fact]
    public async Task ShowsNothingWithoutNetworkAccess()
    {
        var page = await GetAdminPageAsync(new FakeGitHub(null), enabled: true);

        Assert.DoesNotContain("is available", page, StringComparison.Ordinal);
    }

    private async Task<string> GetAdminPageAsync(FakeGitHub gitHub, bool enabled)
    {
        using var app = new PaGettoApplication(_output, gitHub, dict =>
        {
            dict["Authentication:Mode"] = "Local";
            dict["UpdateCheck:Enabled"] = enabled ? "true" : "false";
        });

        // The background service checks at startup too; checking here doesn't wait for it.
        await app.Services.GetRequiredService<ReleaseChecker>().CheckAsync(CancellationToken.None);

        await WebUiSession.SeedLocalUserAsync(app, "admin", Password, isAdmin: true);
        using var admin = await WebUiSession.SignInAsync(app, "admin", Password);

        return await admin.GetStringAsync("/Admin/Feeds");
    }

    /// <summary>
    /// Answers the latest release request with a release of the given tag, or fails like a server
    /// without network access when the tag is null.
    /// </summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly string _tag;
        private int _requestCount;

        public FakeGitHub(string tag)
        {
            _tag = tag;
        }

        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri != GitHubReleaseClient.LatestReleaseUrl)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            Interlocked.Increment(ref _requestCount);
            if (_tag == null)
            {
                throw new HttpRequestException("No such host is known.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{ "tag_name": "{{_tag}}" }""", Encoding.UTF8, "application/json"),
            });
        }
    }
}
