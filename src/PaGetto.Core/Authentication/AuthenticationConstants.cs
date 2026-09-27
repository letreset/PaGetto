namespace PaGetto.Core.Authentication;
public static class AuthenticationConstants
{
    public const string NugetBasicAuthenticationScheme = "NugetBasicAuthentication";
    public const string NugetUserPolicy = "NuGetUserPolicy";
    public const string EntraOidcScheme = "EntraOidc";
    public const string CookieScheme = "PaGettoCookie";
    public const string CookieName = "PaGetto.Auth";

    /// <summary>Claim stamped into the cookie that indicates whether the user is an admin.</summary>
    public const string IsAdminClaim = "pagetto:is_admin";
}
