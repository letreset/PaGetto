using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Covers security headers, configurable CORS and response compression.
/// </summary>
public class HttpHardeningIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public HttpHardeningIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddsSecurityHeadersByDefault()
    {
        using var app = new PaGettoApplication(_output);
        using var client = app.CreateDefaultClient();

        using var response = await client.GetAsync("v3/index.json");

        response.EnsureSuccessStatusCode();
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task OmitsSecurityHeadersWhenDisabled()
    {
        using var app = new PaGettoApplication(_output, inMemoryConfiguration: dict =>
        {
            dict["SecurityHeaders:Enabled"] = "false";
        });
        using var client = app.CreateDefaultClient();

        using var response = await client.GetAsync("v3/index.json");

        response.EnsureSuccessStatusCode();
        Assert.False(response.Headers.Contains("X-Content-Type-Options"));
    }

    [Fact]
    public async Task AllowsAnyOriginByDefault()
    {
        using var app = new PaGettoApplication(_output);
        using var client = app.CreateDefaultClient();

        using var response = await SendWithOriginAsync(client, "https://anywhere.example.com");

        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task RestrictsOriginsWhenConfigured()
    {
        using var app = new PaGettoApplication(_output, inMemoryConfiguration: dict =>
        {
            dict["Cors:AllowedOrigins:0"] = "https://portal.example.com";
        });
        using var client = app.CreateDefaultClient();

        using var allowed = await SendWithOriginAsync(client, "https://portal.example.com");
        using var denied = await SendWithOriginAsync(client, "https://evil.example.com");

        Assert.Equal("https://portal.example.com", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task CompressesJsonResponses()
    {
        using var app = new PaGettoApplication(_output);
        using var client = app.CreateDefaultClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "v3/index.json");
        request.Headers.AcceptEncoding.ParseAdd("gzip");

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
    }

    private static Task<HttpResponseMessage> SendWithOriginAsync(HttpClient client, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "v3/index.json");
        request.Headers.Add("Origin", origin);
        return client.SendAsync(request);
    }
}
