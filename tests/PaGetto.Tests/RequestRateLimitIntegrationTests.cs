using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

public class RequestRateLimitIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public RequestRateLimitIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task DoesNotLimitWhenDisabled()
    {
        using var app = new PaGettoApplication(_output, inMemoryConfiguration: dict =>
        {
            dict["RequestRateLimit:PermitLimit"] = "1";
        });
        using var client = app.CreateDefaultClient();

        for (var i = 0; i < 3; i++)
        {
            using var response = await client.GetAsync("v3/index.json");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Returns429WithRetryAfterWhenLimitIsHit()
    {
        using var app = CreateLimitedApp(permitLimit: 2);
        using var client = app.CreateDefaultClient();

        using var first = await client.GetAsync("v3/index.json");
        using var second = await client.GetAsync("v3/index.json");
        using var third = await client.GetAsync("v3/index.json");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.NotNull(third.Headers.RetryAfter?.Delta);
        Assert.True(third.Headers.RetryAfter.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task NeverLimitsProbes()
    {
        using var app = CreateLimitedApp(permitLimit: 1);
        using var client = app.CreateDefaultClient();

        using var first = await client.GetAsync("v3/index.json");
        using var limited = await client.GetAsync("v3/index.json");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        for (var i = 0; i < 3; i++)
        {
            using var liveness = await client.GetAsync("livez");
            using var health = await client.GetAsync("health");

            Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        }
    }

    [Fact]
    public async Task PartitionsAuthenticatedRequestsByUserName()
    {
        using var app = CreateLimitedApp(permitLimit: 1, dict =>
        {
            dict["Authentication:Credentials:0:Username"] = "alice";
            dict["Authentication:Credentials:0:Password"] = "alice-password";
            dict["Authentication:Credentials:1:Username"] = "bob";
            dict["Authentication:Credentials:1:Password"] = "bob-password";
        });
        using var client = app.CreateDefaultClient();

        using var alice = await SendAsAsync(client, "alice", "alice-password");
        using var aliceLimited = await SendAsAsync(client, "alice", "alice-password");
        using var bob = await SendAsAsync(client, "bob", "bob-password");

        Assert.Equal(HttpStatusCode.OK, alice.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, aliceLimited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bob.StatusCode);
    }

    private PaGettoApplication CreateLimitedApp(int permitLimit, Action<Dictionary<string, string>> configure = null)
    {
        return new PaGettoApplication(_output, inMemoryConfiguration: dict =>
        {
            dict["RequestRateLimit:Enabled"] = "true";
            dict["RequestRateLimit:PermitLimit"] = permitLimit.ToString();
            dict["RequestRateLimit:WindowSeconds"] = "3600";
            dict["HealthCheck:Path"] = "/health";
            configure?.Invoke(dict);
        });
    }

    private static Task<HttpResponseMessage> SendAsAsync(HttpClient client, string username, string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "v3/search");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            AuthenticationConstants.NugetBasicAuthenticationScheme,
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
        return client.SendAsync(request);
    }
}
