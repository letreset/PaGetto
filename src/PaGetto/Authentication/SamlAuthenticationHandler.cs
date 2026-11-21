using System;
using System.Collections.Specialized;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaGetto.Core.Entities;
using SamlHttpRequest = ITfoxtec.Identity.Saml2.Http.HttpRequest;

namespace PaGetto.Authentication;

/// <summary>
/// Signs users in through a SAML 2.0 identity provider: the challenge redirects to it with an
/// authentication request (HTTP-Redirect binding), and its signed response is posted back to
/// <see cref="RemoteAuthenticationOptions.CallbackPath"/> (HTTP-POST binding). The sign-in's
/// properties wait in a cookie named after the request id, which the response's
/// <c>InResponseTo</c> and <c>RelayState</c> have to match, so a response can't be replayed into
/// another browser or answer a request that wasn't made here.
/// </summary>
public class SamlAuthenticationHandler : RemoteAuthenticationHandler<SamlAuthenticationOptions>
{
    private const string RequestCookiePrefix = ".PaGetto.Saml.";
    private const string RelayStateField = "RelayState";

    private readonly SamlConfigurationProvider _configuration;
    private readonly SingleSignOnSyncService _syncService;
    private readonly IDataProtectionProvider _dataProtection;

    public SamlAuthenticationHandler(
        IOptionsMonitor<SamlAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        SamlConfigurationProvider configuration,
        SingleSignOnSyncService syncService,
        IDataProtectionProvider dataProtection)
        : base(options, logger, encoder)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _dataProtection = dataProtection ?? throw new ArgumentNullException(nameof(dataProtection));
    }

    private PropertiesDataFormat StateFormat =>
        new(_dataProtection.CreateProtector(typeof(SamlAuthenticationHandler).FullName, Scheme.Name));

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (string.IsNullOrEmpty(properties.RedirectUri))
            properties.RedirectUri = OriginalPathBase + OriginalPath + Request.QueryString;

        var configuration = await _configuration.GetAsync(Context.RequestAborted);
        var request = new Saml2AuthnRequest(configuration)
        {
            AssertionConsumerServiceUrl = new Uri(BuildRedirectUri(Options.CallbackPath)),
            // Without it, identity providers such as Keycloak answer with the binding of the request.
            ProtocolBinding = ProtocolBindings.HttpPost,
        };

        var requestId = request.Id.Value;
        Response.Cookies.Append(RequestCookiePrefix + requestId, StateFormat.Protect(properties), BuildRequestCookieOptions());

        var binding = new Saml2RedirectBinding { RelayState = requestId };
        binding.Bind(request);
        Response.Redirect(binding.RedirectLocation.OriginalString);
    }

    protected override async Task<HandleRequestResult> HandleRemoteAuthenticateAsync()
    {
        if (!HttpMethods.IsPost(Request.Method) || !Request.HasFormContentType)
            return HandleRequestResult.Fail("The SAML response has to be posted.");

        var form = await Request.ReadFormAsync(Context.RequestAborted);
        var requestId = form[RelayStateField].ToString();
        var cookieName = RequestCookiePrefix + requestId;
        if (string.IsNullOrEmpty(requestId) || !Request.Cookies.TryGetValue(cookieName, out var protectedProperties))
            return HandleRequestResult.Fail("The SAML response doesn't answer a sign-in started in this browser.");

        Response.Cookies.Delete(cookieName, BuildRequestCookieOptions());
        var properties = StateFormat.Unprotect(protectedProperties);
        if (properties == null)
            return HandleRequestResult.Fail("The SAML sign-in state can't be read.");

        var formValues = new NameValueCollection();
        foreach (var field in form)
            formValues.Add(field.Key, field.Value.ToString());

        var samlRequest = new SamlHttpRequest
        {
            Method = Request.Method,
            QueryString = Request.QueryString.Value,
            Query = new NameValueCollection(),
            Form = formValues,
        };

        var configuration = await _configuration.GetAsync(Context.RequestAborted);
        var response = new Saml2AuthnResponse(configuration);
        var binding = new Saml2PostBinding();
        try
        {
            binding.ReadSamlResponse(samlRequest, response);
            if (response.Status != Saml2StatusCodes.Success)
                return HandleRequestResult.Fail($"The identity provider answered '{response.Status}': {response.StatusMessage}", properties);

            // Checks the signature, the audience and the validity period.
            binding.Unbind(samlRequest, response);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HandleRequestResult.Fail(ex, properties);
        }

        if (!string.Equals(response.InResponseToAsString, requestId, StringComparison.Ordinal))
            return HandleRequestResult.Fail("The SAML response answers another request.", properties);

        var identity = new ClaimsIdentity(response.ClaimsIdentity.Claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        try
        {
            await _syncService.OnTokenValidatedAsync(principal, AuthProvider.Saml, Context.RequestAborted);
        }
        catch (UnauthorizedAccessException ex)
        {
            return HandleRequestResult.Fail(ex.Message, properties);
        }

        return HandleRequestResult.Success(new AuthenticationTicket(principal, properties, Scheme.Name));
    }

    // Like the correlation cookie of the other remote schemes: SameSite=None, because the identity
    // provider posts its response from another site.
    private CookieOptions BuildRequestCookieOptions()
    {
        var options = Options.CorrelationCookie.Build(Context, TimeProvider.GetUtcNow() + Options.RemoteAuthenticationTimeout);
        options.Path = OriginalPathBase + Options.CallbackPath;
        return options;
    }
}
