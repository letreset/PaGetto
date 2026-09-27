using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
/// Verifies that push, delete and relist on a read-only feed return 403 to requests with valid
/// credentials, so NuGet clients don't ask for new credentials, and 401 to requests without them.
/// </summary>
public class ReadOnlyFeedTests : IDisposable
{
    private const string LocalUsername = "readonlytest";
    private const string LocalPassword = "ReadOnlyTest123!";
    private const string ConfigApiKey = "read-only-test-key";

    private readonly ITestOutputHelper _output;
    private PaGettoApplication _app;
    private HttpClient _client;

    public ReadOnlyFeedTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public async Task LocalMode_BasicAuth_ReturnsForbidden(string method)
    {
        CreateApp("Local");
        await SeedLocalUserAsync();
        _client.DefaultRequestHeaders.Authorization = BasicAuth(LocalUsername, LocalPassword);

        using var response = await _client.SendAsync(BuildRequest(method));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public async Task LocalMode_PatApiKey_ReturnsForbidden(string method)
    {
        CreateApp("Local");
        var userId = await SeedLocalUserAsync();
        var token = await CreateTokenAsync(userId);

        using var request = BuildRequest(method);
        request.Headers.Add("X-NuGet-ApiKey", token);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public async Task LocalMode_Anonymous_ReturnsUnauthorized(string method)
    {
        CreateApp("Local");

        using var response = await _client.SendAsync(BuildRequest(method));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public async Task LocalMode_WrongPassword_ReturnsUnauthorized(string method)
    {
        CreateApp("Local");
        await SeedLocalUserAsync();
        _client.DefaultRequestHeaders.Authorization = BasicAuth(LocalUsername, "wrong-password");

        using var response = await _client.SendAsync(BuildRequest(method));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public async Task ConfigMode_ValidApiKey_ReturnsForbidden(string method)
    {
        CreateApp("Config");

        using var request = BuildRequest(method);
        request.Headers.Add("X-NuGet-ApiKey", ConfigApiKey);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public async Task ConfigMode_WrongApiKey_ReturnsUnauthorized(string method)
    {
        CreateApp("Config");

        using var request = BuildRequest(method);
        request.Headers.Add("X-NuGet-ApiKey", "wrong-key");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Helpers ---

    private void CreateApp(string authMode)
    {
        _app = new PaGettoApplication(_output, null, dict =>
        {
            dict["Authentication:Mode"] = authMode;
            dict["ApiKey"] = ConfigApiKey;
            dict["IsReadOnlyMode"] = "true";
        });
        _client = _app.CreateClient();
    }

    private static HttpRequestMessage BuildRequest(string method)
    {
        var url = method == "PUT" ? "api/v2/package" : "api/v2/package/TestData/1.2.3";
        return new HttpRequestMessage(new HttpMethod(method), url) { Content = new ByteArrayContent([]) };
    }

    private static AuthenticationHeaderValue BasicAuth(string username, string password)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        return new AuthenticationHeaderValue("Basic", encoded);
    }

    private async Task<Guid> SeedLocalUserAsync()
    {
        using var scope = _app.Services.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var user = await userService.CreateLocalUserAsync(
            LocalUsername, "Read-only Test User", null,
            LocalPassword, canLoginToUI: false,
            createdByUserId: null,
            CancellationToken.None);

        var defaultFeed = await feedService.GetDefaultFeedAsync(CancellationToken.None);
        await permissionService.GrantPermissionAsync(
            user.Id, PrincipalType.User, defaultFeed.Id,
            canPush: true, canPull: true,
            CancellationToken.None, canDelete: true);

        return user.Id;
    }

    private async Task<string> CreateTokenAsync(Guid userId)
    {
        using var scope = _app.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var result = await tokenService.CreateTokenAsync(
            userId, "read-only-test", DateTime.UtcNow.AddDays(30), CancellationToken.None);

        return result.PlaintextToken;
    }

    public void Dispose()
    {
        _client?.Dispose();
        _app?.Dispose();
    }
}
