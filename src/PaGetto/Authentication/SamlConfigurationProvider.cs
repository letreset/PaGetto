using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel.Security;
using System.Threading;
using System.Threading.Tasks;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using ITfoxtec.Identity.Saml2.Schemas.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaGetto.Core.Configuration;

namespace PaGetto.Authentication;

/// <summary>
/// Reads the SAML identity provider's metadata from <see cref="SamlOptions.MetadataUrl"/>: its
/// single sign-on URL and the certificates its responses are signed with. The result is kept for a
/// day, so a renewed certificate is picked up without a restart; when reading fails, the last
/// result stays in use.
/// </summary>
public sealed partial class SamlConfigurationProvider : IDisposable
{
    /// <summary>
    /// The name of the <see cref="HttpClient"/> the metadata is read with.
    /// </summary>
    public const string HttpClientName = "SamlMetadata";

    /// <summary>
    /// How long to wait for the metadata.
    /// </summary>
    public static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(1);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);

    private readonly IOptionsMonitor<NugetAuthenticationOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SamlConfigurationProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private Saml2Configuration _configuration;
    private DateTimeOffset _refreshAt;

    public SamlConfigurationProvider(
        IOptionsMonitor<NugetAuthenticationOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<SamlConfigurationProvider> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The configuration for SAML requests and responses.
    /// </summary>
    /// <exception cref="Exception">The metadata has never been read successfully.</exception>
    public async Task<Saml2Configuration> GetAsync(CancellationToken cancellationToken)
    {
        if (_configuration != null && DateTimeOffset.UtcNow < _refreshAt)
            return _configuration;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_configuration != null && DateTimeOffset.UtcNow < _refreshAt)
                return _configuration;

            var options = _options.CurrentValue.Saml;
            try
            {
                using var client = _httpClientFactory.CreateClient(HttpClientName);
                var metadata = await client.GetStringAsync(options.MetadataUrl, cancellationToken);
                _configuration = Create(options, metadata);
                _refreshAt = DateTimeOffset.UtcNow + RefreshInterval;
            }
            catch (Exception ex) when (_configuration != null && ex is not OperationCanceledException)
            {
                LogMetadataRefreshFailed(ex, options.MetadataUrl);
                _refreshAt = DateTimeOffset.UtcNow + RetryInterval;
            }

            return _configuration;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Makes the configuration from the identity provider's metadata.
    /// </summary>
    /// <exception cref="InvalidOperationException">The metadata lacks what sign-in needs.</exception>
    public static Saml2Configuration Create(SamlOptions options, string metadata)
    {
        var entityDescriptor = new EntityDescriptor();
        entityDescriptor.ReadIdPSsoDescriptor(metadata);

        var idp = entityDescriptor.IdPSsoDescriptor
                  ?? throw new InvalidOperationException("The SAML metadata has no IDPSSODescriptor.");
        var singleSignOn = idp.SingleSignOnServices?.FirstOrDefault(s => s.Binding == ProtocolBindings.HttpRedirect)
                           ?? throw new InvalidOperationException("The SAML metadata has no single sign-on service with the HTTP-Redirect binding.");
        var certificates = idp.SigningCertificates?.ToList() ?? [];
        if (certificates.Count == 0)
            throw new InvalidOperationException("The SAML metadata has no signing certificate.");

        var configuration = new Saml2Configuration
        {
            Issuer = options.EntityId,
            SingleSignOnDestination = singleSignOn.Location,
            // The certificates come from the metadata, which is trusted, rather than from a chain:
            // identity providers usually sign with a self-signed certificate.
            CertificateValidationMode = X509CertificateValidationMode.None,
            RevocationMode = X509RevocationMode.NoCheck,
        };
        configuration.SignatureValidationCertificates.AddRange(certificates);
        configuration.AllowedAudienceUris.Add(options.EntityId);

        return configuration;
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the SAML metadata from {MetadataUrl} failed, the previous metadata stays in use")]
    private partial void LogMetadataRefreshFailed(Exception exception, string metadataUrl);
}
