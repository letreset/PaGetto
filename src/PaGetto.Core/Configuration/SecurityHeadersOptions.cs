namespace PaGetto.Core.Configuration;

public class SecurityHeadersOptions
{
    /// <summary>
    /// Adds X-Content-Type-Options, X-Frame-Options, Referrer-Policy, X-Permitted-Cross-Domain-Policies
    /// and Permissions-Policy headers to every response. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Sends the Strict-Transport-Security header outside of the Development environment.
    /// Only enable this when PaGetto is always served over HTTPS. Default is false.
    /// </summary>
    public bool EnableHsts { get; set; }

    /// <summary>
    /// The HSTS max-age in days. Default is 365.
    /// </summary>
    public int HstsMaxAgeDays { get; set; } = 365;
}
