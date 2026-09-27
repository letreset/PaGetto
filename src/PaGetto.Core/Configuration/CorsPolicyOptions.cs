namespace PaGetto.Core.Configuration;

public class CorsPolicyOptions
{
    /// <summary>
    /// The origins allowed to make cross-origin requests, e.g. "https://portal.example.com".
    /// When empty (the default), any origin is allowed.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>
    /// Allows cross-origin requests to include credentials (cookies, Authorization header).
    /// Requires <see cref="AllowedOrigins"/> to be set. Default is false.
    /// </summary>
    public bool AllowCredentials { get; set; }
}
