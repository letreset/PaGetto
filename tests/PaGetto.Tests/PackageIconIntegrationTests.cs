using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The web UI must fall back to the default icon for packages that have no icon,
/// on the package list and on the package page, including under a feed path.
/// </summary>
public class PackageIconIntegrationTests : IDisposable
{
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;
    private readonly Stream _packageStream;

    public PackageIconIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient();
        _packageStream = TestResources.GetResourceStream(TestResources.Package);
    }

    [Theory]
    [InlineData("/", "/_content/")]
    [InlineData("/packages/TestData", "/_content/")]
    [InlineData("/feeds/default/", "/feeds/default/_content/")]
    [InlineData("/feeds/default/packages/TestData", "/feeds/default/_content/")]
    public async Task PackageWithoutIconShowsItsInitials(string path, string expectedPrefix)
    {
        await _app.AddPackageAsync(_packageStream);

        using var response = await _client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("class=\"bgt-tile[^\"]*\"[^>]*>\\s*TE\\s*</div>", html);
        Assert.DoesNotContain("<img src=\"\"", html);

        // Static assets, like the icon sprite, resolve under the feed's path base.
        Assert.Contains($"<use href=\"{expectedPrefix}PaGetto.Web/images/icons.svg#", html);
    }

    public void Dispose()
    {
        _packageStream.Dispose();
        _client.Dispose();
        _app.Dispose();
    }
}
