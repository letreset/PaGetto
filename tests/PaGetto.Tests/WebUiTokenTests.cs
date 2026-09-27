using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Personal access tokens for local accounts: created on My Tokens or by an administrator,
/// and used as the password by NuGet clients.
/// </summary>
public class WebUiTokenTests : IDisposable
{
    private const string Password = "LocalPassword123!";

    private readonly PaGettoApplication _app;

    public WebUiTokenTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
    }

    [Fact]
    public async Task LocalUser_CreatesTokenOnMyTokens_AndPushesWithIt()
    {
        await WebUiSession.SeedLocalUserAsync(_app, "dev", Password, canPush: true);
        using var session = await WebUiSession.SignInAsync(_app, "dev", Password);

        var menu = await session.GetStringAsync("/Account/Tokens");
        Assert.Contains("Create token", menu);

        using var create = await session.PostFormAsync("/Account/Tokens", "Create", new Dictionary<string, string>
        {
            { "TokenName", "laptop" },
            { "ExpiryDays", "30" },
        });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        var token = ExtractNewToken(await session.GetStringAsync("/Account/Tokens"));

        Assert.Equal(HttpStatusCode.Created, await PushAsync("dev", token));
    }

    [Fact]
    public async Task Admin_CreatesTokenForAccountWithoutWebSignIn_AndItCanPush()
    {
        await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
        var agent = await WebUiSession.SeedLocalUserAsync(_app, "build-agent", Password, canLoginToUI: false, canPush: true);
        using var session = await WebUiSession.SignInAsync(_app, "admin", Password);

        using var create = await session.PostFormAsync("/Admin/Accounts", "CreateToken", new Dictionary<string, string>
        {
            { "userId", agent.Id.ToString() },
            { "tokenName", "ci" },
            { "expiryDays", "90" },
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var body = await create.Content.ReadAsStringAsync();
        Assert.Contains("created for &#x27;build-agent&#x27;", body);

        Assert.Equal(HttpStatusCode.Created, await PushAsync("build-agent", ExtractNewToken(body)));
    }

    [Fact]
    public async Task Revoke_IgnoresTokensOfOtherUsers()
    {
        var victim = await WebUiSession.SeedLocalUserAsync(_app, "victim", Password);
        await WebUiSession.SeedLocalUserAsync(_app, "attacker", Password);

        Guid victimTokenId;
        using (var scope = _app.Services.CreateScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var result = await tokens.CreateTokenAsync(victim.Id, "victim-token", DateTime.UtcNow.AddDays(30), CancellationToken.None);
            victimTokenId = result.Token.Id;
        }

        using var session = await WebUiSession.SignInAsync(_app, "attacker", Password);
        using var revoke = await session.PostFormAsync("/Account/Tokens", "Revoke", new Dictionary<string, string>
        {
            { "tokenId", victimTokenId.ToString() },
        });

        using var verifyScope = _app.Services.CreateScope();
        var victimTokens = await verifyScope.ServiceProvider.GetRequiredService<ITokenService>()
            .GetUserTokensAsync(victim.Id, CancellationToken.None);
        Assert.False(Assert.Single(victimTokens).IsRevoked);
    }

    private async Task<HttpStatusCode> PushAsync(string username, string token)
    {
        using var client = _app.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(TestResources.GetResourceStream(TestResources.Package)), "package", "package.nupkg");

        using var request = new HttpRequestMessage(HttpMethod.Put, "api/v2/package") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{token}")));

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static string ExtractNewToken(string body)
    {
        var match = Regex.Match(body, "<code id=\"new-token-value\">([^<]+)</code>");
        Assert.True(match.Success, "The new token wasn't shown");
        return match.Groups[1].Value;
    }

    public void Dispose()
    {
        _app.Dispose();
    }
}
