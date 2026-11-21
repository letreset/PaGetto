using System;
using System.Collections.Generic;
using System.Linq;
using PaGetto.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace PaGetto;

/// <summary>
/// PaGetto's options configuration, specific to the default PaGetto application.
/// Don't use this if you are embedding PaGetto into your own custom ASP.NET Core application.
/// </summary>
public class ValidatePaGettoOptions
    : IValidateOptions<PaGettoOptions>
{
    private static readonly HashSet<string> _validDatabaseTypes
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MySql",
            "PostgreSql",
            "Sqlite",
            "SqlServer",
        };

    private static readonly HashSet<string> _validStorageTypes
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AliyunOss",
            "AwsS3",
            "AzureBlobStorage",
            "Filesystem",
            "GoogleCloud",
            "TencentCos",
            "Null"
        };

    private static readonly HashSet<string> _validSearchTypes
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AzureSearch",
            "Database",
            "Null",
        };

    // The range accepted for Authentication:MinPasswordLength. bcrypt only reads the first 72 bytes
    // of a password, so a longer minimum would add nothing.
    private const int MinAllowedPasswordLength = 8;
    private const int MaxAllowedPasswordLength = 72;

    private static readonly HashSet<string> _validEmailTypes
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Smtp",
            "Graph",
            "Null",
        };

    public ValidateOptionsResult Validate(string name, PaGettoOptions options)
    {
        var failures = new List<string>();

        if (options.Database == null) failures.Add($"The '{nameof(PaGettoOptions.Database)}' config is required");
#pragma warning disable CS0618 // Read only to reject the removed setting.
        if (!string.IsNullOrEmpty(options.ApiKey))
            failures.Add($"The '{nameof(PaGettoOptions.ApiKey)}' config is no longer supported: move the key to '{nameof(PaGettoOptions.Authentication)}:{nameof(NugetAuthenticationOptions.ApiKeys)}:0:Key'");
#pragma warning restore CS0618
        if (options.Search == null) failures.Add($"The '{nameof(PaGettoOptions.Search)}' config is required");
        if (options.Storage == null) failures.Add($"The '{nameof(PaGettoOptions.Storage)}' config is required");
        if (options.RegistrationPageSize < 1) failures.Add($"The '{nameof(PaGettoOptions.RegistrationPageSize)}' config must be at least 1");
        if (options.UpstreamListingCacheSeconds < 0) failures.Add($"The '{nameof(PaGettoOptions.UpstreamListingCacheSeconds)}' config must be 0 or more");
        if (options.MirrorCacheRetentionDays < 0) failures.Add($"The '{nameof(PaGettoOptions.MirrorCacheRetentionDays)}' config must be 0 or more");
        if (options.Cors is { AllowCredentials: true } && options.Cors.AllowedOrigins is not { Length: > 0 })
        {
            failures.Add($"The '{nameof(PaGettoOptions.Cors)}:{nameof(CorsPolicyOptions.AllowCredentials)}' config requires '{nameof(PaGettoOptions.Cors)}:{nameof(CorsPolicyOptions.AllowedOrigins)}' to be set");
        }
        if (options.SecurityHeaders is { EnableHsts: true, HstsMaxAgeDays: < 1 })
        {
            failures.Add($"The '{nameof(PaGettoOptions.SecurityHeaders)}:{nameof(SecurityHeadersOptions.HstsMaxAgeDays)}' config must be at least 1");
        }

        if (options.Audit is { RetentionDays: < 0 })
        {
            failures.Add($"The '{nameof(PaGettoOptions.Audit)}:{nameof(AuditOptions.RetentionDays)}' config must be 0 or more");
        }

        ValidateRequestRateLimit(options, failures);
        ValidateForwardedHeaders(options, failures);
        ValidateDataProtection(options, failures);

        if (!string.IsNullOrWhiteSpace(options.PublicBaseUrl)
            && !(Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var publicBaseUrl)
                 && (publicBaseUrl.Scheme == Uri.UriSchemeHttp || publicBaseUrl.Scheme == Uri.UriSchemeHttps)))
        {
            failures.Add($"The '{nameof(PaGettoOptions.PublicBaseUrl)}' config must be an absolute http(s) URL");
        }

        if (string.Equals(options.Database?.Type, "AzureTable", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"The '{nameof(PaGettoOptions.Database)}:{nameof(DatabaseOptions.Type)}' value 'AzureTable' is no longer supported, " +
                "because Azure Table Storage can't store feeds, users or permissions. " +
                $"Move to one of these databases first: {string.Join(", ", _validDatabaseTypes)}");
        }
        else if (!_validDatabaseTypes.Contains(options.Database?.Type))
        {
            failures.Add(
                $"The '{nameof(PaGettoOptions.Database)}:{nameof(DatabaseOptions.Type)}' config is invalid. " +
                $"Allowed values: {string.Join(", ", _validDatabaseTypes)}");
        }

        if (!string.IsNullOrWhiteSpace(options.Database?.ServerVersion)
            && !ServerVersion.TryParse(options.Database.ServerVersion, out _))
        {
            failures.Add(
                $"The '{nameof(PaGettoOptions.Database)}:{nameof(DatabaseOptions.ServerVersion)}' config is invalid. " +
                "Expected a MySQL or MariaDB version such as '8.0.36-mysql' or '11.4.2-mariadb'");
        }

        if (!string.IsNullOrWhiteSpace(options.Database?.JournalMode)
            && !DatabaseOptions.SqliteJournalModes.Contains(options.Database.JournalMode, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add(
                $"The '{nameof(PaGettoOptions.Database)}:{nameof(DatabaseOptions.JournalMode)}' config is invalid. " +
                $"Allowed values: {string.Join(", ", DatabaseOptions.SqliteJournalModes)}");
        }

        if (!_validStorageTypes.Contains(options.Storage?.Type))
        {
            failures.Add(
                $"The '{nameof(PaGettoOptions.Storage)}:{nameof(StorageOptions.Type)}' config is invalid. " +
                $"Allowed values: {string.Join(", ", _validStorageTypes)}");
        }

        if (!_validSearchTypes.Contains(options.Search?.Type))
        {
            failures.Add(
                $"The '{nameof(PaGettoOptions.Search)}:{nameof(SearchOptions.Type)}' config is invalid. " +
                $"Allowed values: {string.Join(", ", _validSearchTypes)}");
        }

        ValidateEmail(options, failures);

        ValidateAuthentication(options, failures);

        if (failures.Count != 0) return ValidateOptionsResult.Fail(failures);

        return ValidateOptionsResult.Success;
    }

    private static void ValidateEmail(PaGettoOptions options, List<string> failures)
    {
        var type = options.Email?.Type;

        // Email is optional: an omitted section or empty Type means email is disabled.
        if (string.IsNullOrEmpty(type))
            return;

        if (!_validEmailTypes.Contains(type))
        {
            failures.Add(
                $"The '{nameof(PaGettoOptions.Email)}:{nameof(EmailOptions.Type)}' config is invalid. " +
                $"Allowed values: {string.Join(", ", _validEmailTypes)} (or omit to disable email)");
        }
    }

    private static void ValidateDataProtection(PaGettoOptions options, List<string> failures)
    {
        const string section = nameof(PaGettoOptions.DataProtection);
        var keyProtection = options.DataProtection;

        if (!string.IsNullOrEmpty(keyProtection?.CertificatePath) && !string.IsNullOrEmpty(keyProtection.CertificateThumbprint))
            failures.Add($"Set either '{section}:{nameof(KeyProtectionOptions.CertificatePath)}' or '{section}:{nameof(KeyProtectionOptions.CertificateThumbprint)}', not both");
        else if (!string.IsNullOrEmpty(keyProtection?.CertificatePath) && !System.IO.File.Exists(keyProtection.CertificatePath))
            failures.Add($"The '{section}:{nameof(KeyProtectionOptions.CertificatePath)}' file '{keyProtection.CertificatePath}' doesn't exist");
    }

    private static void ValidateForwardedHeaders(PaGettoOptions options, List<string> failures)
    {
        const string section = nameof(PaGettoOptions.ForwardedHeaders);

        foreach (var proxy in options.ForwardedHeaders?.KnownProxies ?? [])
        {
            if (!System.Net.IPAddress.TryParse(proxy, out _))
                failures.Add($"The '{section}:{nameof(ProxyTrustOptions.KnownProxies)}' value '{proxy}' is not an IP address");
        }

        foreach (var network in options.ForwardedHeaders?.KnownNetworks ?? [])
        {
            if (!System.Net.IPNetwork.TryParse(network, out _))
                failures.Add($"The '{section}:{nameof(ProxyTrustOptions.KnownNetworks)}' value '{network}' is not a network in CIDR notation, e.g. 10.0.0.0/8");
        }
    }

    private static void ValidateRequestRateLimit(PaGettoOptions options, List<string> failures)
    {
        var rateLimit = options.RequestRateLimit;
        if (rateLimit is not { Enabled: true })
            return;

        const string section = nameof(PaGettoOptions.RequestRateLimit);

        if (rateLimit.PermitLimit < 1)
            failures.Add($"The '{section}:{nameof(RequestRateLimitOptions.PermitLimit)}' config must be at least 1");

        if (rateLimit.WindowSeconds < 1)
            failures.Add($"The '{section}:{nameof(RequestRateLimitOptions.WindowSeconds)}' config must be at least 1");

        if (rateLimit.QueueLimit < 0)
            failures.Add($"The '{section}:{nameof(RequestRateLimitOptions.QueueLimit)}' config must not be negative");
    }

    private static void ValidateSaml(NugetAuthenticationOptions auth, List<string> failures)
    {
        const string section = $"{nameof(PaGettoOptions.Authentication)}:{nameof(NugetAuthenticationOptions.Saml)}";
        var saml = auth.Saml;
        if (saml == null)
        {
            failures.Add($"The '{section}' config is required when Authentication Mode is '{auth.Mode}'");
            return;
        }

        if (string.IsNullOrEmpty(saml.MetadataUrl))
        {
            failures.Add($"The '{section}:{nameof(SamlOptions.MetadataUrl)}' config is required");
        }
        else if (!Uri.TryCreate(saml.MetadataUrl, UriKind.Absolute, out var metadataUrl)
            || (metadataUrl.Scheme != Uri.UriSchemeHttps && metadataUrl.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"The '{section}:{nameof(SamlOptions.MetadataUrl)}' config must be an absolute http or https URL");
        }
        else if (metadataUrl.Scheme != Uri.UriSchemeHttps && saml.RequireHttpsMetadata)
        {
            failures.Add($"The '{section}:{nameof(SamlOptions.MetadataUrl)}' config must use https unless " +
                $"'{section}:{nameof(SamlOptions.RequireHttpsMetadata)}' is false");
        }

        if (string.IsNullOrEmpty(saml.EntityId))
            failures.Add($"The '{section}:{nameof(SamlOptions.EntityId)}' config is required");

        if (string.IsNullOrEmpty(saml.CallbackPath) || !saml.CallbackPath.StartsWith('/'))
            failures.Add($"The '{section}:{nameof(SamlOptions.CallbackPath)}' config must start with '/'");

        if (string.IsNullOrEmpty(saml.RoleAttribute))
            failures.Add($"The '{section}:{nameof(SamlOptions.RoleAttribute)}' config is required");
    }

    private static void ValidateLdap(NugetAuthenticationOptions auth, List<string> failures)
    {
        const string section = $"{nameof(PaGettoOptions.Authentication)}:{nameof(NugetAuthenticationOptions.Ldap)}";
        var ldap = auth.Ldap;
        if (ldap == null)
            return;

        if (!auth.IsLocalEnabled)
        {
            failures.Add($"The '{section}' config is only used in '{nameof(AuthenticationMode.Local)}' and '{nameof(AuthenticationMode.Hybrid)}' mode, " +
                $"not in '{auth.Mode}' mode");
            return;
        }

        if (string.IsNullOrEmpty(ldap.Server))
            failures.Add($"The '{section}:{nameof(LdapOptions.Server)}' config is required");

        if (ldap.Port is < 0 or > 65535)
            failures.Add($"The '{section}:{nameof(LdapOptions.Port)}' config must be between 0 and 65535");

        if (!Enum.IsDefined(ldap.Security))
        {
            failures.Add($"The '{section}:{nameof(LdapOptions.Security)}' config must be " +
                $"'{nameof(LdapSecurity.Ssl)}', '{nameof(LdapSecurity.StartTls)}' or '{nameof(LdapSecurity.None)}'");
        }

        if (string.IsNullOrEmpty(ldap.SearchBase))
            failures.Add($"The '{section}:{nameof(LdapOptions.SearchBase)}' config is required");

        if (ldap.UserFilter?.Contains("{0}", StringComparison.Ordinal) != true)
            failures.Add($"The '{section}:{nameof(LdapOptions.UserFilter)}' config must contain '{{0}}', which is replaced with the username");

        if (!string.IsNullOrEmpty(ldap.GroupSearchFilter) && !ldap.GroupSearchFilter.Contains("{0}", StringComparison.Ordinal))
            failures.Add($"The '{section}:{nameof(LdapOptions.GroupSearchFilter)}' config must contain '{{0}}', which is replaced with the user's DN");

        if (string.IsNullOrEmpty(ldap.UsernameAttribute))
            failures.Add($"The '{section}:{nameof(LdapOptions.UsernameAttribute)}' config is required");

        if (string.IsNullOrEmpty(ldap.IdAttribute))
            failures.Add($"The '{section}:{nameof(LdapOptions.IdAttribute)}' config is required");

        if (ldap.CacheSeconds < 0)
            failures.Add($"The '{section}:{nameof(LdapOptions.CacheSeconds)}' config must not be negative");

        if (ldap.TimeoutSeconds < 1)
            failures.Add($"The '{section}:{nameof(LdapOptions.TimeoutSeconds)}' config must be at least 1");
    }

    private static void ValidateOidc(NugetAuthenticationOptions auth, List<string> failures)
    {
        const string section = $"{nameof(PaGettoOptions.Authentication)}:{nameof(NugetAuthenticationOptions.Oidc)}";
        var oidc = auth.Oidc;
        if (oidc == null)
        {
            failures.Add($"The '{section}' config is required when Authentication Mode is '{auth.Mode}'");
            return;
        }

        if (string.IsNullOrEmpty(oidc.Authority))
        {
            failures.Add($"The '{section}:{nameof(OidcOptions.Authority)}' config is required");
        }
        else if (!Uri.TryCreate(oidc.Authority, UriKind.Absolute, out var authority)
            || (authority.Scheme != Uri.UriSchemeHttps && authority.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"The '{section}:{nameof(OidcOptions.Authority)}' config must be an absolute http or https URL");
        }
        else if (authority.Scheme != Uri.UriSchemeHttps && oidc.RequireHttpsMetadata)
        {
            failures.Add($"The '{section}:{nameof(OidcOptions.Authority)}' config must use https unless " +
                $"'{section}:{nameof(OidcOptions.RequireHttpsMetadata)}' is false");
        }

        if (string.IsNullOrEmpty(oidc.ClientId))
            failures.Add($"The '{section}:{nameof(OidcOptions.ClientId)}' config is required");

        var scopes = (oidc.Scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!scopes.Contains("openid", StringComparer.Ordinal))
            failures.Add($"The '{section}:{nameof(OidcOptions.Scopes)}' config must include 'openid'");

        if (string.IsNullOrEmpty(oidc.CallbackPath) || !oidc.CallbackPath.StartsWith('/'))
            failures.Add($"The '{section}:{nameof(OidcOptions.CallbackPath)}' config must start with '/'");

        if (string.IsNullOrEmpty(oidc.RoleClaim))
            failures.Add($"The '{section}:{nameof(OidcOptions.RoleClaim)}' config is required");
    }

    private static void ValidateAuthentication(PaGettoOptions options, List<string> failures)
    {
        var auth = options.Authentication;
        if (auth == null || !Enum.IsDefined(auth.Mode))
        {
            failures.Add($"The '{nameof(PaGettoOptions.Authentication)}:{nameof(NugetAuthenticationOptions.Mode)}' config is required: " +
                $"'{nameof(AuthenticationMode.Local)}', '{nameof(AuthenticationMode.Entra)}', '{nameof(AuthenticationMode.Oidc)}', '{nameof(AuthenticationMode.Saml)}', '{nameof(AuthenticationMode.Hybrid)}' or '{nameof(AuthenticationMode.Legacy)}'");
            return;
        }

        var mode = auth.Mode;

        if (mode == AuthenticationMode.Legacy)
            return;

        if (mode == AuthenticationMode.Hybrid && new object[] { auth.Entra, auth.Oidc, auth.Saml }.Count(section => section != null) > 1)
        {
            failures.Add($"Only one of the '{nameof(NugetAuthenticationOptions.Entra)}', '{nameof(NugetAuthenticationOptions.Oidc)}' and " +
                $"'{nameof(NugetAuthenticationOptions.Saml)}' configs can be set in Hybrid mode: single sign-on goes through one provider");
        }
        else if (auth.SingleSignOnProvider == Core.Entities.AuthProvider.Oidc)
        {
            ValidateOidc(auth, failures);
        }
        else if (auth.SingleSignOnProvider == Core.Entities.AuthProvider.Saml)
        {
            ValidateSaml(auth, failures);
        }
        else if (auth.SingleSignOnProvider == Core.Entities.AuthProvider.Entra)
        {
            if (auth.Entra == null)
            {
                failures.Add($"The '{nameof(NugetAuthenticationOptions.Entra)}' config is required when Authentication Mode is '{mode}'");
            }
            else
            {
                if (string.IsNullOrEmpty(auth.Entra.TenantId))
                    failures.Add($"The '{nameof(EntraOptions.TenantId)}' config is required for Entra authentication");

                if (string.IsNullOrEmpty(auth.Entra.ClientId))
                    failures.Add($"The '{nameof(EntraOptions.ClientId)}' config is required for Entra authentication");

                if (string.IsNullOrEmpty(auth.Entra.Instance))
                    failures.Add($"The '{nameof(EntraOptions.Instance)}' config is required for Entra authentication");
            }
        }

        ValidateLdap(auth, failures);

        if (auth.MaxTokenExpiryDays < 1)
            failures.Add($"The '{nameof(NugetAuthenticationOptions.MaxTokenExpiryDays)}' config must be at least 1");

        if (auth.MaxFailedAttempts < 1)
            failures.Add($"The '{nameof(NugetAuthenticationOptions.MaxFailedAttempts)}' config must be at least 1");

        if (auth.LockoutMinutes < 1)
            failures.Add($"The '{nameof(NugetAuthenticationOptions.LockoutMinutes)}' config must be at least 1");

        if (auth.SessionTimeoutMinutes < 1)
            failures.Add($"The '{nameof(NugetAuthenticationOptions.SessionTimeoutMinutes)}' config must be at least 1");

        if (auth.MinPasswordLength is < MinAllowedPasswordLength or > MaxAllowedPasswordLength)
            failures.Add($"The '{nameof(NugetAuthenticationOptions.MinPasswordLength)}' config must be between {MinAllowedPasswordLength} and {MaxAllowedPasswordLength}");
    }
}
