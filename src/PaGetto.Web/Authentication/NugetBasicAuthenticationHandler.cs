using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using PaGetto.Core.Configuration;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Web.Extensions;

namespace PaGetto.Web.Authentication;

public partial class NugetBasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IOptions<PaGettoOptions> _pagettoOptions;
    private readonly IFeedAuthenticationService _feedAuthService;

    public NugetBasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<PaGettoOptions> pagettoOptions,
        IFeedAuthenticationService feedAuthService)
        : base(options, logger, encoder)
    {
        _pagettoOptions = pagettoOptions;
        _feedAuthService = feedAuthService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authMode = _pagettoOptions.Value.Authentication?.Mode ?? AuthenticationMode.Legacy;

        if (authMode == AuthenticationMode.Legacy)
        {
            // Static auth mode: use configured credentials
            return await HandleStaticAuthAsync();
        }

        // New mode: use database-backed authentication
        return await HandleNewAuthenticateAsync();
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // The pub client only reads a Bearer challenge, and shows its message to the user. sbt
        // matches credentials to the realm, so Maven feeds have one that doesn't say NuGet, and
        // PyPI, Go and RubyGems feeds use it too. Cargo shows the login URL of its challenge.
        var endpointType = Context.GetEndpoint()?.Metadata.GetMetadata<FeedTypeAttribute>()?.Type;

        Response.Headers.WWWAuthenticate = endpointType switch
        {
            FeedType.Pub => PubBearerChallenge($"Authentication is required. Add a personal access token with: dart pub token add {Request.Scheme}://{Request.Host}{Request.PathBase}"),
            FeedType.Maven or FeedType.PyPI or FeedType.Go or FeedType.RubyGems => $"Basic realm=\"{MavenRealm}\"",
            FeedType.Cargo => $"Cargo login_url=\"{Request.Scheme}://{Request.Host}{Request.PathBase}/connect\"",
            _ => "Basic realm=\"NuGet Server\"",
        };

        await base.HandleChallengeAsync(properties);
    }

    /// <summary>
    /// The realm of the Basic challenge of Maven, PyPI, Go and RubyGems feeds, which sbt credentials name.
    /// </summary>
    public const string MavenRealm = "PaGetto";

    /// <summary>
    /// The <c>WWW-Authenticate</c> value the pub client expects with a 401 or 403 response.
    /// </summary>
    public static string PubBearerChallenge(string message)
    {
        // The message is a quoted string, so it can't contain quotes itself.
        return $"Bearer realm=\"pub\", message=\"{message.Replace('"', '\'')}\"";
    }

    /// <summary>
    /// A signed-in caller without pull access gets 404, the same as for a feed that doesn't
    /// exist, so feed slugs can't be probed.
    /// </summary>
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = (int)HttpStatusCode.NotFound;
        return Task.CompletedTask;
    }

    private Task<AuthenticateResult> HandleStaticAuthAsync()
    {
        if (IsOpenAccessAllowed())
            return CreateAnonymousAuthenticationResult();

        if (!Request.Headers.TryGetValue("Authorization", out var auth))
            return Task.FromResult(AuthenticateResult.NoResult());

        string username;
        string password;
        try
        {
            var authHeader = AuthenticationHeaderValue.Parse(auth);
            var credentialBytes = Convert.FromBase64String(authHeader.Parameter!);
            var credentials = Encoding.UTF8.GetString(credentialBytes).Split([':'], 2);
            username = credentials[0];
            password = credentials[1];
        }
        catch
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header"));
        }

        if (!ValidateStaticCredentials(username, password))
            return Task.FromResult(AuthenticateResult.Fail("Invalid Username or Password"));

        return CreateUserAuthenticationResult(username, null);
    }

    private async Task<AuthenticateResult> HandleNewAuthenticateAsync()
    {
        // Try X-NuGet-ApiKey first (used by dotnet nuget push -k <token>)
        var apiKey = Request.Headers[HttpRequestExtensions.ApiKeyHeader].ToString();
        if (!string.IsNullOrEmpty(apiKey))
        {
            var tokenResult = await _feedAuthService.AuthenticateByTokenAsync(apiKey, Context.RequestAborted);
            if (tokenResult.IsAuthenticated)
            {
                LogApiKeyLoginSuccess(Logger, "LoginSuccess", tokenResult.Username, tokenResult.UserId, Context.Connection.RemoteIpAddress);
                return await CreateUserAuthenticationResult(tokenResult.Username, tokenResult.UserId?.ToString());
            }
        }

        if (!Request.Headers.TryGetValue("Authorization", out var auth))
            return AuthenticateResult.NoResult();

        // Cargo sends a personal access token as the whole header value, without a scheme.
        var authValue = auth.ToString();
        if (authValue.StartsWith(TokenService.TokenPrefix, StringComparison.Ordinal) && !authValue.Contains(' ', StringComparison.Ordinal))
        {
            return await HandleBearerTokenAsync(authValue);
        }

        string username;
        string password;
        try
        {
            var authHeader = AuthenticationHeaderValue.Parse(auth);

            // A personal access token sent as a bearer token (dart pub token add).
            if (string.Equals(authHeader.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleBearerTokenAsync(authHeader.Parameter);
            }

            var credentialBytes = Convert.FromBase64String(authHeader.Parameter!);
            var credentials = Encoding.UTF8.GetString(credentialBytes).Split([':'], 2);
            username = credentials[0];
            password = credentials[1];
        }
        catch
        {
            return AuthenticateResult.Fail("Invalid Authorization Header");
        }

        var result = await _feedAuthService.AuthenticateByCredentialsAsync(
            username, password, Context.RequestAborted);

        if (!result.IsAuthenticated)
        {
            var failIp = Context.Connection.RemoteIpAddress?.ToString();
            LogLoginFailure(Logger, "LoginFailure", username, failIp);
            return AuthenticateResult.Fail("Invalid Username or Password");
        }

        var ip = Context.Connection.RemoteIpAddress?.ToString();
        LogLoginSuccess(Logger, "LoginSuccess", result.Username, result.UserId, ip);

        return await CreateUserAuthenticationResult(result.Username, result.UserId?.ToString());
    }

    private async Task<AuthenticateResult> HandleBearerTokenAsync(string token)
    {
        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.Fail("Invalid Authorization Header");

        var result = await _feedAuthService.AuthenticateByTokenAsync(token, Context.RequestAborted);
        if (!result.IsAuthenticated)
            return AuthenticateResult.Fail("Invalid token");

        LogApiKeyLoginSuccess(Logger, "LoginSuccess", result.Username, result.UserId, Context.Connection.RemoteIpAddress);
        return await CreateUserAuthenticationResult(result.Username, result.UserId?.ToString());
    }

    private Task<AuthenticateResult> CreateAnonymousAuthenticationResult()
    {
        Claim[] claims = [new Claim(ClaimTypes.Anonymous, string.Empty)];
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);

        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private Task<AuthenticateResult> CreateUserAuthenticationResult(string username, string userId)
    {
        var claims = new System.Collections.Generic.List<Claim>
        {
            new(ClaimTypes.Name, username)
        };

        if (userId != null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);

        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private bool IsOpenAccessAllowed()
    {
        return _pagettoOptions.Value.Authentication is null ||
            _pagettoOptions.Value.Authentication.Credentials is null ||
            _pagettoOptions.Value.Authentication.Credentials.Length == 0 ||
            _pagettoOptions.Value.Authentication.Credentials.All(a => string.IsNullOrWhiteSpace(a.Username) && string.IsNullOrWhiteSpace(a.Password));
    }

    private bool ValidateStaticCredentials(string username, string password)
    {
        return _pagettoOptions.Value.Authentication.Credentials.Any(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && a.Password == password);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit: {EventType} - User {Username} ({UserId}) authenticated via API key from {IP}")]
    private static partial void LogApiKeyLoginSuccess(ILogger logger, string eventType, string username, Guid? userId, IPAddress ip);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit: {EventType} - Authentication failed for {Username} from {IP}")]
    private static partial void LogLoginFailure(ILogger logger, string eventType, string username, string ip);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit: {EventType} - User {Username} ({UserId}) authenticated from {IP}")]
    private static partial void LogLoginSuccess(ILogger logger, string eventType, string username, Guid? userId, string ip);
}
