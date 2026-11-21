using Microsoft.AspNetCore.Authentication;

namespace PaGetto.Authentication;

/// <summary>
/// The options of <see cref="SamlAuthenticationHandler"/>. The identity provider's settings come
/// from <c>Authentication:Saml</c> through <see cref="SamlConfigurationProvider"/>.
/// </summary>
public class SamlAuthenticationOptions : RemoteAuthenticationOptions
{
    public SamlAuthenticationOptions()
    {
        Events = new RemoteAuthenticationEvents();
    }
}
