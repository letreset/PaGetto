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
        if (options.Cors is { AllowCredentials: true } && options.Cors.AllowedOrigins is not { Length: > 0 })
        {
            failures.Add($"The '{nameof(PaGettoOptions.Cors)}:{nameof(CorsPolicyOptions.AllowCredentials)}' config requires '{nameof(PaGettoOptions.Cors)}:{nameof(CorsPolicyOptions.AllowedOrigins)}' to be set");
        }
        if (options.SecurityHeaders is { EnableHsts: true, HstsMaxAgeDays: < 1 })
        {
            failures.Add($"The '{nameof(PaGettoOptions.SecurityHeaders)}:{nameof(SecurityHeadersOptions.HstsMaxAgeDays)}' config must be at least 1");
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

    private static void ValidateAuthentication(PaGettoOptions options, List<string> failures)
    {
        var auth = options.Authentication;
        if (auth == null || !Enum.IsDefined(auth.Mode))
        {
            failures.Add($"The '{nameof(PaGettoOptions.Authentication)}:{nameof(NugetAuthenticationOptions.Mode)}' config is required: " +
                $"'{nameof(AuthenticationMode.Local)}', '{nameof(AuthenticationMode.Entra)}', '{nameof(AuthenticationMode.Hybrid)}' or '{nameof(AuthenticationMode.Legacy)}'");
            return;
        }

        var mode = auth.Mode;

        if (mode == AuthenticationMode.Legacy)
            return;

        if (mode is AuthenticationMode.Entra or AuthenticationMode.Hybrid)
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
