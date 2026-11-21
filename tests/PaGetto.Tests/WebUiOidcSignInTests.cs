using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Tests.Support;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Sign-in through a generic OpenID Connect provider (<c>Authentication:Mode</c> <c>Oidc</c>, or
/// <c>Hybrid</c> with <c>Authentication:Oidc</c>).
/// </summary>
public class WebUiOidcSignInTests
{
    private const string Authority = "https://sso.example.com/realms/pagetto";
    private const string AuthorizationEndpoint = Authority + "/protocol/openid-connect/auth";

    private readonly ITestOutputHelper _output;

    public WebUiOidcSignInTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task LoginPageOffersTheProviderWithoutPasswordForm()
    {
        using var app = CreateApplication("Oidc");
        using var client = CreateClient(app);

        var body = await client.GetStringAsync("/Login");

        Assert.Contains("Sign in with Keycloak", body);
        Assert.Contains("handler=SingleSignOn", body);
        Assert.DoesNotContain("type=\"password\"", body);
    }

    [Fact]
    public async Task HybridLoginPageOffersTheProviderAndLocalAccounts()
    {
        using var app = CreateApplication("Hybrid");
        using var client = CreateClient(app);

        var body = await client.GetStringAsync("/Login");

        Assert.Contains("Sign in with Keycloak", body);
        Assert.Contains("type=\"password\"", body);
        Assert.DoesNotContain("Sign in with Microsoft", body);
    }

    [Theory]
    [InlineData("Oidc")]
    [InlineData("Hybrid")]
    public async Task SingleSignOnRedirectsToTheProvider(string mode)
    {
        using var app = CreateApplication(mode);
        using var client = CreateClient(app);

        using var response = await client.GetAsync("/Login?handler=SingleSignOn&ReturnUrl=%2FAccount%2FTokens");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal(AuthorizationEndpoint, location.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("pagetto", query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("openid profile email", query["scope"]);
        Assert.Equal("http://localhost/signin-oidc", query["redirect_uri"]);
        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
    }

    private WebApplicationFactory<Startup> CreateApplication(string mode)
    {
        var app = new PaGettoApplication(_output, null, dict =>
        {
            dict["Authentication:Mode"] = mode;
            dict["Authentication:Oidc:Authority"] = Authority;
            dict["Authentication:Oidc:ClientId"] = "pagetto";
            dict["Authentication:Oidc:ClientSecret"] = "test-secret";
            dict["Authentication:Oidc:DisplayName"] = "Keycloak";
        });

        // The provider's metadata, so the test doesn't fetch its discovery document.
        return app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<OpenIdConnectOptions>(AuthenticationConstants.OidcScheme, options =>
            {
                options.Configuration = new OpenIdConnectConfiguration
                {
                    Issuer = Authority,
                    AuthorizationEndpoint = AuthorizationEndpoint,
                    TokenEndpoint = Authority + "/protocol/openid-connect/token",
                };
            })));
    }

    private static HttpClient CreateClient(WebApplicationFactory<Startup> app)
    {
        return app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }
}
