using PaGetto.Authentication;
using PaGetto.Core;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Web.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PaGetto;

internal static partial class ServiceCollectionExtensions
{
    internal static PaGettoApplication AddNugetBasicHttpAuthentication(this PaGettoApplication app)
    {
        app.Services.AddAuthentication(options =>
        {
            // Breaks existing tests if the contains check is not here.
            if (!options.SchemeMap.ContainsKey(AuthenticationConstants.NugetBasicAuthenticationScheme))
            {
                options.AddScheme<NugetBasicAuthenticationHandler>(AuthenticationConstants.NugetBasicAuthenticationScheme, AuthenticationConstants.NugetBasicAuthenticationScheme);
                options.DefaultAuthenticateScheme = AuthenticationConstants.NugetBasicAuthenticationScheme;
                options.DefaultChallengeScheme = AuthenticationConstants.NugetBasicAuthenticationScheme;
            }
        });

        return app;
    }

    internal static PaGettoApplication AddNugetBasicHttpAuthorization(this PaGettoApplication app, Action<AuthorizationPolicyBuilder> configurePolicy = null)
    {
        app.Services.AddScoped<IAuthorizationHandler, FeedPermissionHandler>();

        app.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthenticationConstants.NugetUserPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new FeedPermissionRequirement(FeedPermissionRequirement.Pull));
                configurePolicy?.Invoke(policy);
            });
        });

        return app;
    }

    /// <summary>
    /// Registers the cookie-based web session, and single sign-on through Entra ID or another
    /// OpenID Connect provider when the authentication mode includes it (Entra, Oidc or Hybrid).
    /// </summary>
    internal static PaGettoApplication AddWebUiAuthentication(
        this PaGettoApplication app,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        // Read the authentication mode from configuration to decide whether to register single sign-on
        var authSection = configuration.GetSection("Authentication");
        var modeString = authSection?.GetValue<string>("Mode");

        if (!Enum.TryParse<AuthenticationMode>(modeString, ignoreCase: true, out var mode))
            mode = AuthenticationMode.Legacy;

		if (mode == AuthenticationMode.Legacy)
            return app;

        var sessionTimeoutMinutes = authSection.GetValue(nameof(NugetAuthenticationOptions.SessionTimeoutMinutes), new NugetAuthenticationOptions().SessionTimeoutMinutes);
        var entraSection = authSection.GetSection(nameof(NugetAuthenticationOptions.Entra));
        var entraOptions = entraSection.Get<EntraOptions>() ?? new EntraOptions();
        var oidcOptions = authSection.GetSection(nameof(NugetAuthenticationOptions.Oidc)).Get<OidcOptions>();
        var samlOptions = authSection.GetSection(nameof(NugetAuthenticationOptions.Saml)).Get<SamlOptions>();
        var singleSignOnProvider = new NugetAuthenticationOptions { Mode = mode, Oidc = oidcOptions, Saml = samlOptions }.SingleSignOnProvider;
        oidcOptions ??= new OidcOptions();
        samlOptions ??= new SamlOptions();

        var authBuilder = app.Services.AddAuthentication(options =>
            {
                // Keep NugetBasicAuth as the default for NuGet feed API requests.
                // OIDC + Cookie are used for interactive browser sessions only.
                options.DefaultScheme = AuthenticationConstants.NugetBasicAuthenticationScheme;
            });

        string oidcScheme = null;
        if (singleSignOnProvider == AuthProvider.Entra)
        {
            oidcScheme = AuthenticationConstants.EntraOidcScheme;
            authBuilder.AddMicrosoftIdentityWebApp(entraSection, oidcScheme, AuthenticationConstants.CookieScheme);
        }
        else if (singleSignOnProvider == AuthProvider.Oidc)
        {
            oidcScheme = AuthenticationConstants.OidcScheme;
            authBuilder
                .AddCookie(AuthenticationConstants.CookieScheme)
                .AddOpenIdConnect(oidcScheme, options =>
                {
                    options.SignInScheme = AuthenticationConstants.CookieScheme;
                    options.Authority = oidcOptions.Authority;
                    options.ClientId = oidcOptions.ClientId;
                    options.ClientSecret = oidcOptions.ClientSecret;
                    options.CallbackPath = oidcOptions.CallbackPath;
                    options.RequireHttpsMetadata = oidcOptions.RequireHttpsMetadata;
                    options.Scope.Clear();
                    foreach (var scope in (oidcOptions.Scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        options.Scope.Add(scope);
                });
        }
        else if (singleSignOnProvider == AuthProvider.Saml)
        {
            app.Services.AddSingleton<SamlConfigurationProvider>();
            app.Services.AddHttpClient(SamlConfigurationProvider.HttpClientName, client => client.Timeout = SamlConfigurationProvider.MetadataTimeout);
            authBuilder
                .AddCookie(AuthenticationConstants.CookieScheme)
                .AddRemoteScheme<SamlAuthenticationOptions, SamlAuthenticationHandler>(
                    AuthenticationConstants.SamlScheme,
                    samlOptions.DisplayName,
                    options =>
                    {
                        options.SignInScheme = AuthenticationConstants.CookieScheme;
                        options.CallbackPath = samlOptions.CallbackPath;
                        options.Events.OnRemoteFailure = HandleRemoteFailureAsync;
                    });
        }
        else
        {
            authBuilder.AddCookie(AuthenticationConstants.CookieScheme);
        }

        // When a request has the session cookie but no Authorization header (i.e. a browser
        // session after OIDC sign-in), forward authentication to the cookie scheme so the
        // identity is actually read. Without this the default NugetBasicAuth handler sees no
        // Authorization header and returns NoResult, leaving the user unauthenticated.
        app.Services.Configure<AuthenticationSchemeOptions>(
            AuthenticationConstants.NugetBasicAuthenticationScheme, options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                if (!context.Request.Headers.ContainsKey("Authorization")
                    && context.Request.Cookies.ContainsKey(AuthenticationConstants.CookieName))
                {
                    return AuthenticationConstants.CookieScheme;
                }
                return null;
            };

            // Forbid stays on this scheme (404, see NugetBasicAuthenticationHandler) instead of
            // the cookie scheme's redirect, so browsers can't tell a forbidden feed from a missing one.
            options.ForwardForbid = AuthenticationConstants.NugetBasicAuthenticationScheme;
        });

        // Configure the cookie scheme
        app.Services.Configure<CookieAuthenticationOptions>(AuthenticationConstants.CookieScheme, options =>
        {
            options.LoginPath = "/Login";
            options.LogoutPath = "/Logout";
            options.AccessDeniedPath = "/Login";
            options.Cookie.Name = AuthenticationConstants.CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest //For development, allow non-secure cookies over HTTP to simplify testing.
                : Microsoft.AspNetCore.Http.CookieSecurePolicy.Always; //In production, require secure cookies to ensure they are only sent over HTTPS.
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(sessionTimeoutMinutes);
            options.SlidingExpiration = true;

            options.Events ??= new CookieAuthenticationEvents();
            options.Events.OnValidatePrincipal = async context =>
            {
                var userIdClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(AuthenticationConstants.CookieScheme);
                    return;
                }

                var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
                var user = await userService.FindByIdAsync(userId, context.HttpContext.RequestAborted);
                if (user == null || !user.IsEnabled || !user.CanLoginToUI)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(AuthenticationConstants.CookieScheme);
                    return;
                }

                // Refresh the IsAdmin and MustChangePassword claims so changes take effect without requiring re-login
                var identity = context.Principal?.Identity as ClaimsIdentity;
                if (identity != null)
                {
                    var existing = identity.FindFirst(AuthenticationConstants.IsAdminClaim);
                    if (existing != null)
                        identity.RemoveClaim(existing);
                    identity.AddClaim(new Claim(AuthenticationConstants.IsAdminClaim, user.IsAdmin ? "true" : "false"));

                    // A renamed local account shows its new name right away.
                    if (user.AuthProvider == AuthProvider.Local)
                    {
                        ReplaceClaim(identity, ClaimTypes.Name, user.Username);
                        ReplaceClaim(identity, "DisplayName", user.DisplayName ?? user.Username);
                    }

                    var mustChange = identity.FindFirst(AuthenticationConstants.MustChangePasswordClaim);
                    if (mustChange != null)
                        identity.RemoveClaim(mustChange);
                    if (user.MustChangePassword)
                        identity.AddClaim(new Claim(AuthenticationConstants.MustChangePasswordClaim, "true"));

                    context.ReplacePrincipal(new ClaimsPrincipal(identity));
                    context.ShouldRenew = true;
                }
            };
        });

        app.Services.AddScoped<SingleSignOnSyncService>();

        if (oidcScheme == null)
            return app;

        // Configure the OIDC options for role-based authentication
        app.Services.Configure<OpenIdConnectOptions>(oidcScheme, options =>
        {
            // Use authorization code flow instead of implicit flow so that
            // "ID tokens" does not need to be enabled under Implicit grant
            // in the Azure app registration.
            options.ResponseType = Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectResponseType.Code;
            options.MapInboundClaims = false; // Don't map claims to Microsoft-specific claim types, keep the original claim types from the token.

            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = singleSignOnProvider == AuthProvider.Entra
                ? entraOptions.RoleClaim
                : oidcOptions.RoleClaim;

            options.Events.OnRemoteFailure = HandleRemoteFailureAsync;

            var existingOnTokenValidated = options.Events.OnTokenValidated;

            options.Events.OnTokenValidated = async context =>
            {
                if (existingOnTokenValidated != null)
                    await existingOnTokenValidated(context);

                var syncService = context.HttpContext.RequestServices.GetRequiredService<SingleSignOnSyncService>();
                try
                {
                    await syncService.OnTokenValidatedAsync(context.Principal, singleSignOnProvider.Value, context.HttpContext.RequestAborted);
                }
                catch (UnauthorizedAccessException ex)
                {
                    context.Fail(ex.Message);
                }
            };
        });

        return app;
    }

    // A failed or refused single sign-on goes back to the sign-in page instead of an error page.
    private static Task HandleRemoteFailureAsync(RemoteFailureContext context)
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(ServiceCollectionExtensions));
        LogRemoteFailure(logger, context.Failure);

        context.Response.Redirect("/Login?error=authentication_failed");
        context.HandleResponse();
        return Task.CompletedTask;
    }

    private static void ReplaceClaim(ClaimsIdentity identity, string type, string value)
    {
        var existing = identity.FindFirst(type);
        if (existing != null)
            identity.RemoveClaim(existing);
        identity.AddClaim(new Claim(type, value));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Single sign-on remote failure.")]
    private static partial void LogRemoteFailure(ILogger logger, Exception exception);
}
