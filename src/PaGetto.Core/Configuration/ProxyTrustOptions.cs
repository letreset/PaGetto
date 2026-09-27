namespace PaGetto.Core.Configuration;

/// <summary>
/// Which reverse proxies may set X-Forwarded-For, X-Forwarded-Proto and X-Forwarded-Host.
/// Bound to the <c>ForwardedHeaders</c> section.
/// </summary>
public class ProxyTrustOptions
{
    /// <summary>
    /// IP addresses of trusted proxies, e.g. "10.0.0.5".
    /// </summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>
    /// Networks of trusted proxies in CIDR notation, e.g. "10.0.0.0/8".
    /// </summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>
    /// When neither list is set, the forwarded headers of every client are accepted (the default).
    /// </summary>
    public bool TrustsAllProxies => KnownProxies is not { Length: > 0 } && KnownNetworks is not { Length: > 0 };
}
