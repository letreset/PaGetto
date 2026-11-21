using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using ITfoxtec.Identity.Saml2.Schemas.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens.Saml2;
using PaGetto.Authentication;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Sign-in through a SAML 2.0 identity provider (<c>Authentication:Mode</c> <c>Saml</c>, or
/// <c>Hybrid</c> with <c>Authentication:Saml</c>). The identity provider is played by the test: its
/// metadata is served to PaGetto, and its responses are signed with a certificate made here.
/// </summary>
public partial class WebUiSamlSignInTests : IDisposable
{
    private const string IdpEntityId = "https://idp.example.test/realms/pagetto";
    private const string IdpSingleSignOnUrl = IdpEntityId + "/protocol/saml";
    private const string MetadataUrl = IdpEntityId + "/protocol/saml/descriptor";
    private const string EntityId = "https://pagetto.example.test";
    private const string CallbackUrl = "https://localhost/signin-saml";

    private readonly ITestOutputHelper _output;
    private readonly X509Certificate2 _idpCertificate = CreateCertificate("CN=idp.example.test");
    private readonly List<IDisposable> _disposables = [];

    public WebUiSamlSignInTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task LoginPageOffersTheProviderWithoutPasswordForm()
    {
        using var client = CreateClient(CreateApplication("Saml"));

        var body = await client.GetStringAsync("/Login");

        Assert.Contains("Sign in with Keycloak", body);
        Assert.Contains("handler=SingleSignOn", body);
        Assert.DoesNotContain("type=\"password\"", body);
    }

    [Fact]
    public async Task HybridLoginPageOffersTheProviderAndLocalAccounts()
    {
        using var client = CreateClient(CreateApplication("Hybrid"));

        var body = await client.GetStringAsync("/Login");

        Assert.Contains("Sign in with Keycloak", body);
        Assert.Contains("type=\"password\"", body);
    }

    [Theory]
    [InlineData("Saml")]
    [InlineData("Hybrid")]
    public async Task SingleSignOnRedirectsToTheIdentityProvider(string mode)
    {
        using var client = CreateClient(CreateApplication(mode));

        using var response = await client.GetAsync("/Login?handler=SingleSignOn&ReturnUrl=%2FAccount%2FTokens");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal(IdpSingleSignOnUrl, location.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.StartsWith("_", query["RelayState"].ToString());

        // The redirect binding deflates the request.
        using var deflated = new DeflateStream(new MemoryStream(Convert.FromBase64String(query["SAMLRequest"].ToString())), CompressionMode.Decompress);
        using var reader = new StreamReader(deflated);
        var request = await reader.ReadToEndAsync();
        Assert.Contains($"AssertionConsumerServiceURL=\"{CallbackUrl}\"", request);
        Assert.Contains($"ProtocolBinding=\"{ProtocolBindings.HttpPost.OriginalString}\"", request);
        Assert.Contains($">{EntityId}</", request);
    }

    [Fact]
    public async Task SignedResponseSignsTheUserInAndSyncsItsGroups()
    {
        var app = CreateApplication("Saml");
        var groupId = await CreateGroupAsync(app, "Crew", "ship_crew");
        using var client = CreateClient(app);
        var requestId = await StartSignInAsync(client);

        using var response = await PostResponseAsync(client, requestId, CreateResponse(requestId, "Admin", "ship_crew"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Tokens", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(AuthenticationConstants.CookieName + "=", StringComparison.Ordinal));

        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();
        var groups = scope.ServiceProvider.GetRequiredService<IGroupService>();
        var user = await users.FindByUsernameAsync("fry@planetexpress.com", CancellationToken.None);
        Assert.Equal(AuthProvider.Saml, user.AuthProvider);
        Assert.Equal("fry", user.EntraObjectId);
        Assert.Equal("Philip J. Fry", user.DisplayName);
        Assert.True(user.IsAdmin);
        Assert.Contains(groupId, (await groups.GetUserGroupsAsync(user.Id, CancellationToken.None)).Select(g => g.Id));

        using var tokens = await client.GetAsync("/Account/Tokens");
        Assert.Equal(HttpStatusCode.OK, tokens.StatusCode);
    }

    [Fact]
    public async Task ResponseSignedByAnotherCertificateIsRefused()
    {
        using var client = CreateClient(CreateApplication("Saml"));
        var requestId = await StartSignInAsync(client);
        using var otherCertificate = CreateCertificate("CN=attacker.example.test");

        using var response = await PostResponseAsync(client, requestId, CreateResponse(requestId, otherCertificate));

        AssertRefused(response);
    }

    [Fact]
    public async Task UnsignedResponseIsRefused()
    {
        using var client = CreateClient(CreateApplication("Saml"));
        var requestId = await StartSignInAsync(client);

        using var response = await PostResponseAsync(client, requestId, CreateResponse(requestId, signingCertificate: null));

        AssertRefused(response);
    }

    [Fact]
    public async Task ResponseToAnotherRequestIsRefused()
    {
        using var client = CreateClient(CreateApplication("Saml"));
        var requestId = await StartSignInAsync(client);

        using var response = await PostResponseAsync(client, requestId, CreateResponse("_another-request"));

        AssertRefused(response);
    }

    [Fact]
    public async Task ResponseWithoutTheSignInCookieIsRefused()
    {
        var app = CreateApplication("Saml");
        using var client = CreateClient(app);
        var requestId = await StartSignInAsync(client);

        // Another browser, which didn't start the sign-in.
        using var otherBrowser = CreateClient(app);
        using var response = await PostResponseAsync(otherBrowser, requestId, CreateResponse(requestId));

        AssertRefused(response);
    }

    [Fact]
    public async Task NewUserWhoseUsernameBelongsToALocalAccountIsRefused()
    {
        var app = CreateApplication("Hybrid");
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserService>();
            await users.CreateLocalUserAsync("fry@planetexpress.com", "Local Fry", null, "LocalPassword123!", true, null, CancellationToken.None);
        }

        using var client = CreateClient(app);
        var requestId = await StartSignInAsync(client);

        using var response = await PostResponseAsync(client, requestId, CreateResponse(requestId));

        AssertRefused(response);
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
        _idpCertificate.Dispose();
    }

    private static void AssertRefused(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Login?error=authentication_failed", response.Headers.Location?.OriginalString);
        Assert.DoesNotContain(
            response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            c => c.StartsWith(AuthenticationConstants.CookieName + "=", StringComparison.Ordinal));
    }

    private WebApplicationFactory<Startup> CreateApplication(string mode)
    {
        var metadata = CreateMetadata();
        var app = new PaGettoApplication(_output, null, dict =>
        {
            dict["Authentication:Mode"] = mode;
            dict["Authentication:Saml:MetadataUrl"] = MetadataUrl;
            dict["Authentication:Saml:EntityId"] = EntityId;
            dict["Authentication:Saml:DisplayName"] = "Keycloak";
        }).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(SamlConfigurationProvider.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new MetadataHandler(metadata))));

        _disposables.Add(app);
        return app;
    }

    // Over https, like a browser: the sign-in cookie is Secure, so it is only sent back over https.
    private static HttpClient CreateClient(WebApplicationFactory<Startup> app)
    {
        return app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });
    }

    private static async Task<string> StartSignInAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/Login?handler=SingleSignOn&ReturnUrl=%2FAccount%2FTokens");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return QueryHelpers.ParseQuery(response.Headers.Location!.Query)["RelayState"].ToString();
    }

    private static async Task<HttpResponseMessage> PostResponseAsync(HttpClient client, string relayState, string samlResponse)
    {
        return await client.PostAsync(CallbackUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SAMLResponse"] = samlResponse,
            ["RelayState"] = relayState,
        }));
    }

    private string CreateMetadata()
    {
        var entityDescriptor = new EntityDescriptor(new Saml2Configuration { Issuer = IdpEntityId })
        {
            IdPSsoDescriptor = new IdPSsoDescriptor
            {
                SigningCertificates = [_idpCertificate],
                SingleSignOnServices =
                [
                    new SingleSignOnService { Binding = ProtocolBindings.HttpRedirect, Location = new Uri(IdpSingleSignOnUrl) },
                ],
            },
        };

        return new Saml2Metadata(entityDescriptor).CreateMetadata().ToXml();
    }

    private string CreateResponse(string inResponseTo, params string[] groups)
    {
        return CreateResponse(inResponseTo, _idpCertificate, groups);
    }

    // The base64 SAMLResponse an identity provider posts, signed with signingCertificate.
    private static string CreateResponse(string inResponseTo, X509Certificate2 signingCertificate, params string[] groups)
    {
        var configuration = new Saml2Configuration { Issuer = IdpEntityId, SigningCertificate = signingCertificate };
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "fry"),
            new("email", "fry@planetexpress.com"),
            new("name", "Philip J. Fry"),
        };
        claims.AddRange(groups.Select(group => new Claim("groups", group)));

        var response = new Saml2AuthnResponse(configuration)
        {
            InResponseTo = new Saml2Id(inResponseTo),
            Status = Saml2StatusCodes.Success,
            Destination = new Uri(CallbackUrl),
            NameId = new Saml2NameIdentifier("fry", NameIdentifierFormats.Persistent),
            ClaimsIdentity = new ClaimsIdentity(claims),
        };
        response.CreateSecurityToken(EntityId);

        var binding = new Saml2PostBinding();
        binding.Bind(response);
        return SamlResponseField().Match(binding.PostContent).Groups[1].Value;
    }

    private static X509Certificate2 CreateCertificate(string subject)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    private static async Task<Guid> CreateGroupAsync(WebApplicationFactory<Startup> app, string name, string roleValue)
    {
        using var scope = app.Services.CreateScope();
        var groups = scope.ServiceProvider.GetRequiredService<IGroupService>();
        return (await groups.CreateGroupAsync(name, roleValue, null, CancellationToken.None)).Id;
    }

    [GeneratedRegex("name=\"SAMLResponse\" value=\"([^\"]+)\"")]
    private static partial Regex SamlResponseField();

    private sealed class MetadataHandler : HttpMessageHandler
    {
        private readonly string _metadata;

        public MetadataHandler(string metadata)
        {
            _metadata = metadata;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = request.RequestUri?.ToString() == MetadataUrl
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_metadata, Encoding.UTF8, "application/xml") }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }
}
