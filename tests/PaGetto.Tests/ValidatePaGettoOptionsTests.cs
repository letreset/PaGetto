using System.Collections.Generic;
using System.Linq;
using PaGetto.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace PaGetto.Tests;

public class ValidatePaGettoOptionsTests
{
    public class ValidateEmail
    {
        private static bool HasEmailTypeFailure(PaGettoOptions options)
        {
            var result = new ValidatePaGettoOptions().Validate(null, options);
            return result.Failed
                && result.Failures.Any(f => f.Contains($"{nameof(PaGettoOptions.Email)}:{nameof(EmailOptions.Type)}"));
        }

        [Theory]
        [InlineData("Smtp")]
        [InlineData("Graph")]
        [InlineData("Null")]
        [InlineData("smtp")] // case-insensitive
        public void AcceptsValidEmailType(string type)
        {
            Assert.False(HasEmailTypeFailure(new PaGettoOptions { Email = new EmailOptions { Type = type } }));
        }

        [Fact]
        public void AcceptsMissingEmailSection()
        {
            Assert.False(HasEmailTypeFailure(new PaGettoOptions()));
        }

        [Fact]
        public void AcceptsEmptyEmailType()
        {
            Assert.False(HasEmailTypeFailure(new PaGettoOptions { Email = new EmailOptions { Type = "" } }));
        }

        [Theory]
        [InlineData("smpt")]
        [InlineData("sendgrid")]
        public void RejectsInvalidEmailType(string type)
        {
            Assert.True(HasEmailTypeFailure(new PaGettoOptions { Email = new EmailOptions { Type = type } }));
        }
    }

    public class ValidateHttp
    {
        private static bool HasFailure(PaGettoOptions options, string key)
        {
            var result = new ValidatePaGettoOptions().Validate(null, options);
            return result.Failed && result.Failures.Any(f => f.Contains(key));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void RejectsRegistrationPageSizeBelowOne(int pageSize)
        {
            Assert.True(HasFailure(new PaGettoOptions { RegistrationPageSize = pageSize }, nameof(PaGettoOptions.RegistrationPageSize)));
        }

        [Fact]
        public void AcceptsDefaultRegistrationPageSize()
        {
            Assert.False(HasFailure(new PaGettoOptions(), nameof(PaGettoOptions.RegistrationPageSize)));
        }

        [Fact]
        public void RejectsNegativeUpstreamListingCacheSeconds()
        {
            Assert.True(HasFailure(new PaGettoOptions { UpstreamListingCacheSeconds = -1 }, nameof(PaGettoOptions.UpstreamListingCacheSeconds)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(300)]
        public void AcceptsUpstreamListingCacheSecondsOfZeroOrMore(int seconds)
        {
            Assert.False(HasFailure(new PaGettoOptions { UpstreamListingCacheSeconds = seconds }, nameof(PaGettoOptions.UpstreamListingCacheSeconds)));
        }

        [Fact]
        public void RejectsNegativeAuditRetentionDays()
        {
            var options = new PaGettoOptions { Audit = new AuditOptions { RetentionDays = -1 } };

            Assert.True(HasFailure(options, $"{nameof(PaGettoOptions.Audit)}:{nameof(AuditOptions.RetentionDays)}"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(30)]
        public void AcceptsAuditRetentionDaysOfZeroOrMore(int days)
        {
            var options = new PaGettoOptions { Audit = new AuditOptions { RetentionDays = days } };

            Assert.False(HasFailure(options, $"{nameof(PaGettoOptions.Audit)}:{nameof(AuditOptions.RetentionDays)}"));
        }

        [Fact]
        public void RejectsCorsCredentialsWithoutOrigins()
        {
            var options = new PaGettoOptions { Cors = new CorsPolicyOptions { AllowCredentials = true } };

            Assert.True(HasFailure(options, $"{nameof(PaGettoOptions.Cors)}:{nameof(CorsPolicyOptions.AllowCredentials)}"));
        }

        [Fact]
        public void AcceptsCorsCredentialsWithOrigins()
        {
            var options = new PaGettoOptions
            {
                Cors = new CorsPolicyOptions { AllowCredentials = true, AllowedOrigins = ["https://portal.example.com"] },
            };

            Assert.False(HasFailure(options, $"{nameof(PaGettoOptions.Cors)}:{nameof(CorsPolicyOptions.AllowCredentials)}"));
        }

        [Fact]
        public void RejectsHstsWithNonPositiveMaxAge()
        {
            var options = new PaGettoOptions { SecurityHeaders = new SecurityHeadersOptions { EnableHsts = true, HstsMaxAgeDays = 0 } };

            Assert.True(HasFailure(options, nameof(SecurityHeadersOptions.HstsMaxAgeDays)));
        }
    }

    public class ValidateRequestRateLimit
    {
        private static bool HasFailure(RequestRateLimitOptions rateLimit, string key)
        {
            var result = new ValidatePaGettoOptions().Validate(null, new PaGettoOptions { RequestRateLimit = rateLimit });
            return result.Failed && result.Failures.Any(f => f.Contains($"{nameof(PaGettoOptions.RequestRateLimit)}:{key}"));
        }

        [Fact]
        public void AcceptsDefaultsWhenEnabled()
        {
            Assert.False(HasFailure(new RequestRateLimitOptions { Enabled = true }, string.Empty));
        }

        [Fact]
        public void IgnoresInvalidValuesWhenDisabled()
        {
            var rateLimit = new RequestRateLimitOptions { PermitLimit = 0, WindowSeconds = 0, QueueLimit = -1 };

            Assert.False(HasFailure(rateLimit, string.Empty));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void RejectsPermitLimitBelowOne(int permitLimit)
        {
            var rateLimit = new RequestRateLimitOptions { Enabled = true, PermitLimit = permitLimit };

            Assert.True(HasFailure(rateLimit, nameof(RequestRateLimitOptions.PermitLimit)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void RejectsWindowSecondsBelowOne(int windowSeconds)
        {
            var rateLimit = new RequestRateLimitOptions { Enabled = true, WindowSeconds = windowSeconds };

            Assert.True(HasFailure(rateLimit, nameof(RequestRateLimitOptions.WindowSeconds)));
        }

        [Fact]
        public void RejectsNegativeQueueLimit()
        {
            var rateLimit = new RequestRateLimitOptions { Enabled = true, QueueLimit = -1 };

            Assert.True(HasFailure(rateLimit, nameof(RequestRateLimitOptions.QueueLimit)));
        }
    }

    public class ValidateAuthenticationMode
    {
        private static bool HasModeFailure(NugetAuthenticationOptions authentication)
        {
            var options = new PaGettoOptions { Authentication = authentication };
            var result = new ValidatePaGettoOptions().Validate(null, options);
            return result.Failed && result.Failures.Any(f => f.Contains("'Authentication:Mode' config is required"));
        }

        [Fact]
        public void RejectsMissingSection()
        {
            Assert.True(HasModeFailure(null));
        }

        [Fact]
        public void RejectsMissingMode()
        {
            Assert.True(HasModeFailure(new NugetAuthenticationOptions()));
        }

        [Theory]
        [InlineData(AuthenticationMode.Legacy)]
        [InlineData(AuthenticationMode.Local)]
        [InlineData(AuthenticationMode.Oidc)]
        [InlineData(AuthenticationMode.Saml)]
        public void AcceptsMode(AuthenticationMode mode)
        {
            Assert.False(HasModeFailure(new NugetAuthenticationOptions { Mode = mode }));
        }

        [Theory]
        [InlineData("Legacy")]
        [InlineData("Config")]
        public void BindsLegacyAndItsOldName(string value)
        {
            var options = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string> { ["Authentication:Mode"] = value })
                .Build()
                .Get<PaGettoOptions>();

            Assert.Equal(AuthenticationMode.Legacy, options.Authentication.Mode);
        }
    }

    public class ValidateOidc
    {
        // The single sign-on failures only: the other sections are left out on purpose.
        private static IReadOnlyList<string> Failures(NugetAuthenticationOptions authentication)
        {
            var options = new PaGettoOptions { Authentication = authentication };
            var result = new ValidatePaGettoOptions().Validate(null, options);
            return result.Failed
                ? result.Failures.Where(f => f.Contains("Oidc") || f.Contains("Entra")).ToList()
                : [];
        }

        private static OidcOptions ValidOidc()
        {
            return new OidcOptions { Authority = "https://sso.example.com/realms/pagetto", ClientId = "pagetto" };
        }

        [Theory]
        [InlineData(AuthenticationMode.Oidc)]
        [InlineData(AuthenticationMode.Hybrid)]
        public void AcceptsValidSettings(AuthenticationMode mode)
        {
            Assert.Empty(Failures(new NugetAuthenticationOptions { Mode = mode, Oidc = ValidOidc() }));
        }

        [Fact]
        public void RequiresTheSectionInOidcMode()
        {
            var failures = Failures(new NugetAuthenticationOptions { Mode = AuthenticationMode.Oidc });

            Assert.Contains(failures, f => f.Contains("'Authentication:Oidc' config is required"));
        }

        [Fact]
        public void RejectsEntraAndOidcTogetherInHybridMode()
        {
            var failures = Failures(new NugetAuthenticationOptions
            {
                Mode = AuthenticationMode.Hybrid,
                Entra = new EntraOptions { Instance = "https://login.microsoftonline.com/", TenantId = "t", ClientId = "c" },
                Oidc = ValidOidc(),
            });

            Assert.Contains(failures, f => f.Contains("can be set in Hybrid mode"));
        }

        [Fact]
        public void RequiresAuthorityAndClientId()
        {
            var failures = Failures(new NugetAuthenticationOptions { Mode = AuthenticationMode.Oidc, Oidc = new OidcOptions() });

            Assert.Contains(failures, f => f.Contains("'Authentication:Oidc:Authority' config is required"));
            Assert.Contains(failures, f => f.Contains("'Authentication:Oidc:ClientId' config is required"));
        }

        [Theory]
        [InlineData("sso.example.com")]
        [InlineData("ftp://sso.example.com")]
        public void RejectsAuthorityThatIsNotAnHttpUrl(string authority)
        {
            var oidc = ValidOidc();
            oidc.Authority = authority;

            var failures = Failures(new NugetAuthenticationOptions { Mode = AuthenticationMode.Oidc, Oidc = oidc });

            Assert.Contains(failures, f => f.Contains("must be an absolute http or https URL"));
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void RequiresHttpsAuthorityUnlessTurnedOff(bool requireHttpsMetadata, bool fails)
        {
            var oidc = ValidOidc();
            oidc.Authority = "http://keycloak:8080/realms/pagetto";
            oidc.RequireHttpsMetadata = requireHttpsMetadata;

            var failures = Failures(new NugetAuthenticationOptions { Mode = AuthenticationMode.Oidc, Oidc = oidc });

            Assert.Equal(fails, failures.Any(f => f.Contains("must use https")));
        }

        [Fact]
        public void RequiresTheOpenIdScope()
        {
            var oidc = ValidOidc();
            oidc.Scopes = "profile email";

            var failures = Failures(new NugetAuthenticationOptions { Mode = AuthenticationMode.Oidc, Oidc = oidc });

            Assert.Contains(failures, f => f.Contains("'Authentication:Oidc:Scopes' config must include 'openid'"));
        }

        [Fact]
        public void RejectsInvalidCallbackPathAndEmptyRoleClaim()
        {
            var oidc = ValidOidc();
            oidc.CallbackPath = "signin-oidc";
            oidc.RoleClaim = "";

            var failures = Failures(new NugetAuthenticationOptions { Mode = AuthenticationMode.Oidc, Oidc = oidc });

            Assert.Contains(failures, f => f.Contains("'Authentication:Oidc:CallbackPath' config must start with '/'"));
            Assert.Contains(failures, f => f.Contains("'Authentication:Oidc:RoleClaim' config is required"));
        }

        [Fact]
        public void IgnoresTheSectionInLocalMode()
        {
            Assert.Empty(Failures(new NugetAuthenticationOptions { Mode = AuthenticationMode.Local, Oidc = new OidcOptions() }));
        }
    }

    public class ValidateSaml
    {
        // The single sign-on failures only: the other sections are left out on purpose.
        private static IReadOnlyList<string> Failures(NugetAuthenticationOptions authentication)
        {
            var options = new PaGettoOptions { Authentication = authentication };
            var result = new ValidatePaGettoOptions().Validate(null, options);
            return result.Failed
                ? result.Failures.Where(f => f.Contains("Saml") || f.Contains("Entra") || f.Contains("Oidc")).ToList()
                : [];
        }

        private static SamlOptions ValidSaml()
        {
            return new SamlOptions
            {
                MetadataUrl = "https://sso.example.com/realms/pagetto/protocol/saml/descriptor",
                EntityId = "https://packages.example.com",
            };
        }

        [Theory]
        [InlineData(AuthenticationMode.Saml)]
        [InlineData(AuthenticationMode.Hybrid)]
        public void AcceptsValidSettings(AuthenticationMode mode)
        {
            Assert.Empty(Failures(new NugetAuthenticationOptions { Mode = mode, Saml = ValidSaml() }));
        }

        [Fact]
        public void RequiresTheSectionInSamlMode()
        {
            var failures = Failures(new NugetAuthenticationOptions { Mode = AuthenticationMode.Saml });

            Assert.Contains(failures, f => f.Contains("'Authentication:Saml' config is required"));
        }

        [Fact]
        public void RejectsOidcAndSamlTogetherInHybridMode()
        {
            var failures = Failures(new NugetAuthenticationOptions
            {
                Mode = AuthenticationMode.Hybrid,
                Oidc = new OidcOptions { Authority = "https://sso.example.com/realms/pagetto", ClientId = "pagetto" },
                Saml = ValidSaml(),
            });

            Assert.Contains(failures, f => f.Contains("can be set in Hybrid mode"));
        }

        [Fact]
        public void RequiresMetadataUrlAndEntityId()
        {
            var failures = Failures(new NugetAuthenticationOptions { Mode = AuthenticationMode.Saml, Saml = new SamlOptions() });

            Assert.Contains(failures, f => f.Contains("'Authentication:Saml:MetadataUrl' config is required"));
            Assert.Contains(failures, f => f.Contains("'Authentication:Saml:EntityId' config is required"));
        }

        [Theory]
        [InlineData("sso.example.com/descriptor")]
        [InlineData("ftp://sso.example.com/descriptor")]
        public void RejectsMetadataUrlThatIsNotAnHttpUrl(string metadataUrl)
        {
            var saml = ValidSaml();
            saml.MetadataUrl = metadataUrl;

            var failures = Failures(new NugetAuthenticationOptions { Mode = AuthenticationMode.Saml, Saml = saml });

            Assert.Contains(failures, f => f.Contains("must be an absolute http or https URL"));
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void RequiresHttpsMetadataUnlessTurnedOff(bool requireHttpsMetadata, bool fails)
        {
            var saml = ValidSaml();
            saml.MetadataUrl = "http://keycloak:8080/realms/pagetto/protocol/saml/descriptor";
            saml.RequireHttpsMetadata = requireHttpsMetadata;

            var failures = Failures(new NugetAuthenticationOptions { Mode = AuthenticationMode.Saml, Saml = saml });

            Assert.Equal(fails, failures.Any(f => f.Contains("must use https")));
        }

        [Fact]
        public void RejectsInvalidCallbackPathAndEmptyRoleAttribute()
        {
            var saml = ValidSaml();
            saml.CallbackPath = "signin-saml";
            saml.RoleAttribute = "";

            var failures = Failures(new NugetAuthenticationOptions { Mode = AuthenticationMode.Saml, Saml = saml });

            Assert.Contains(failures, f => f.Contains("'Authentication:Saml:CallbackPath' config must start with '/'"));
            Assert.Contains(failures, f => f.Contains("'Authentication:Saml:RoleAttribute' config is required"));
        }
    }

    public class ValidateLdap
    {
        private static IReadOnlyList<string> Failures(NugetAuthenticationOptions authentication)
        {
            var options = new PaGettoOptions { Authentication = authentication };
            var result = new ValidatePaGettoOptions().Validate(null, options);
            return result.Failed
                ? result.Failures.Where(f => f.Contains("Ldap")).ToList()
                : [];
        }

        private static LdapOptions ValidLdap()
        {
            return new LdapOptions { Server = "ldap.example.com", SearchBase = "ou=people,dc=example,dc=com" };
        }

        [Theory]
        [InlineData(AuthenticationMode.Local)]
        [InlineData(AuthenticationMode.Hybrid)]
        public void AcceptsValidSettings(AuthenticationMode mode)
        {
            Assert.Empty(Failures(new NugetAuthenticationOptions { Mode = mode, Ldap = ValidLdap() }));
        }

        [Theory]
        [InlineData(AuthenticationMode.Entra)]
        [InlineData(AuthenticationMode.Oidc)]
        [InlineData(AuthenticationMode.Saml)]
        public void RejectsTheSectionInModesWithoutLocalAccounts(AuthenticationMode mode)
        {
            var failures = Failures(new NugetAuthenticationOptions { Mode = mode, Ldap = ValidLdap() });

            Assert.Contains(failures, f => f.Contains("'Authentication:Ldap' config is only used in 'Local' and 'Hybrid' mode"));
        }

        [Fact]
        public void RequiresServerAndSearchBase()
        {
            var failures = Failures(new NugetAuthenticationOptions { Mode = AuthenticationMode.Local, Ldap = new LdapOptions() });

            Assert.Contains(failures, f => f.Contains("'Authentication:Ldap:Server' config is required"));
            Assert.Contains(failures, f => f.Contains("'Authentication:Ldap:SearchBase' config is required"));
        }

        [Fact]
        public void RequiresThePlaceholderInTheFilters()
        {
            var ldap = ValidLdap();
            ldap.UserFilter = "(uid=fry)";
            ldap.GroupSearchFilter = "(objectClass=groupOfNames)";

            var failures = Failures(new NugetAuthenticationOptions { Mode = AuthenticationMode.Local, Ldap = ldap });

            Assert.Contains(failures, f => f.Contains("'Authentication:Ldap:UserFilter' config must contain '{0}'"));
            Assert.Contains(failures, f => f.Contains("'Authentication:Ldap:GroupSearchFilter' config must contain '{0}'"));
        }

        [Fact]
        public void RejectsInvalidNumbersAndEmptyAttributes()
        {
            var ldap = ValidLdap();
            ldap.Port = 70000;
            ldap.Security = (LdapSecurity)7;
            ldap.CacheSeconds = -1;
            ldap.TimeoutSeconds = 0;
            ldap.UsernameAttribute = "";
            ldap.IdAttribute = null;

            var failures = Failures(new NugetAuthenticationOptions { Mode = AuthenticationMode.Local, Ldap = ldap });

            Assert.Contains(failures, f => f.Contains("'Authentication:Ldap:Port' config must be between 0 and 65535"));
            Assert.Contains(failures, f => f.Contains("'Authentication:Ldap:Security' config must be"));
            Assert.Contains(failures, f => f.Contains("'Authentication:Ldap:CacheSeconds' config must not be negative"));
            Assert.Contains(failures, f => f.Contains("'Authentication:Ldap:TimeoutSeconds' config must be at least 1"));
            Assert.Contains(failures, f => f.Contains("'Authentication:Ldap:UsernameAttribute' config is required"));
            Assert.Contains(failures, f => f.Contains("'Authentication:Ldap:IdAttribute' config is required"));
        }
    }

    public class ValidateForwardedHeaders
    {
        private static bool HasFailure(ProxyTrustOptions trust)
        {
            var options = new PaGettoOptions { ForwardedHeaders = trust };
            var result = new ValidatePaGettoOptions().Validate(null, options);
            return result.Failed && result.Failures.Any(f => f.Contains(nameof(PaGettoOptions.ForwardedHeaders)));
        }

        [Fact]
        public void AcceptsValidValues()
        {
            Assert.False(HasFailure(new ProxyTrustOptions { KnownProxies = ["10.0.0.5", "::1"], KnownNetworks = ["10.0.0.0/8"] }));
        }

        [Fact]
        public void RejectsInvalidProxy()
        {
            Assert.True(HasFailure(new ProxyTrustOptions { KnownProxies = ["proxy.local"] }));
        }

        [Fact]
        public void RejectsInvalidNetwork()
        {
            Assert.True(HasFailure(new ProxyTrustOptions { KnownNetworks = ["10.0.0.0"] }));
        }
    }

    public class ValidateAuthenticationLimits
    {
        private static bool HasFailure(NugetAuthenticationOptions authentication, string setting)
        {
            var options = new PaGettoOptions { Authentication = authentication };
            var result = new ValidatePaGettoOptions().Validate(null, options);
            return result.Failed && result.Failures.Any(f => f.Contains(setting));
        }

        [Theory]
        [InlineData(7, true)]
        [InlineData(8, false)]
        [InlineData(72, false)]
        [InlineData(73, true)]
        public void ChecksMinPasswordLength(int length, bool fails)
        {
            var authentication = new NugetAuthenticationOptions { Mode = AuthenticationMode.Local, MinPasswordLength = length };

            Assert.Equal(fails, HasFailure(authentication, nameof(NugetAuthenticationOptions.MinPasswordLength)));
        }

        [Fact]
        public void RejectsSessionTimeoutBelowOneMinute()
        {
            var authentication = new NugetAuthenticationOptions { Mode = AuthenticationMode.Local, SessionTimeoutMinutes = 0 };

            Assert.True(HasFailure(authentication, nameof(NugetAuthenticationOptions.SessionTimeoutMinutes)));
        }
    }

    public class ValidateDataProtection
    {
        private static bool HasFailure(KeyProtectionOptions keyProtection)
        {
            var options = new PaGettoOptions { DataProtection = keyProtection };
            var result = new ValidatePaGettoOptions().Validate(null, options);
            return result.Failed && result.Failures.Any(f => f.Contains(nameof(PaGettoOptions.DataProtection)));
        }

        [Fact]
        public void AcceptsNoCertificate()
        {
            Assert.False(HasFailure(new KeyProtectionOptions()));
        }

        [Fact]
        public void RejectsMissingFile()
        {
            Assert.True(HasFailure(new KeyProtectionOptions { CertificatePath = "does-not-exist.pfx" }));
        }

        [Fact]
        public void RejectsPathAndThumbprint()
        {
            Assert.True(HasFailure(new KeyProtectionOptions { CertificatePath = "a.pfx", CertificateThumbprint = "ABC" }));
        }
    }

    public class ValidatePublicBaseUrl
    {
        [Theory]
        [InlineData("https://packages.example.com", false)]
        [InlineData("packages.example.com", true)]
        [InlineData("ftp://packages.example.com", true)]
        public void RequiresAnAbsoluteHttpUrl(string url, bool fails)
        {
            var result = new ValidatePaGettoOptions().Validate(null, new PaGettoOptions { PublicBaseUrl = url });

            Assert.Equal(fails, result.Failed && result.Failures.Any(f => f.Contains(nameof(PaGettoOptions.PublicBaseUrl))));
        }
    }

    public class ValidateRemovedApiKey
    {
        [Fact]
        public void RejectsTopLevelApiKey()
        {
            var options = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string> { ["ApiKey"] = "secret" })
                .Build()
                .Get<PaGettoOptions>();

            var result = new ValidatePaGettoOptions().Validate(null, options);

            Assert.Contains(result.Failures, f => f.Contains("'ApiKey' config is no longer supported"));
        }
    }

    public class ValidateDatabaseType
    {
        private static IEnumerable<string> DatabaseTypeFailures(string type)
        {
            var options = new PaGettoOptions { Database = new DatabaseOptions { Type = type } };
            var result = new ValidatePaGettoOptions().Validate(null, options);
            return result.Failed
                ? result.Failures.Where(f => f.Contains($"{nameof(PaGettoOptions.Database)}:{nameof(DatabaseOptions.Type)}"))
                : [];
        }

        [Theory]
        [InlineData("Sqlite")]
        [InlineData("SqlServer")]
        [InlineData("PostgreSql")]
        [InlineData("MySql")]
        public void AcceptsSqlDatabases(string type)
        {
            Assert.Empty(DatabaseTypeFailures(type));
        }

        [Theory]
        [InlineData("AzureTable")]
        [InlineData("azuretable")]
        public void RejectsAzureTableAndNamesTheAlternatives(string type)
        {
            var failure = Assert.Single(DatabaseTypeFailures(type));
            Assert.Contains("no longer supported", failure);
            Assert.Contains("Sqlite", failure);
        }
    }

    public class ValidateDatabaseServerVersion
    {
        private static bool HasServerVersionFailure(string serverVersion)
        {
            var options = new PaGettoOptions
            {
                Database = new DatabaseOptions { Type = "MySql", ServerVersion = serverVersion },
            };
            var result = new ValidatePaGettoOptions().Validate(null, options);
            return result.Failed
                && result.Failures.Any(f => f.Contains($"{nameof(PaGettoOptions.Database)}:{nameof(DatabaseOptions.ServerVersion)}"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("8.0.36-mysql")]
        [InlineData("11.4.2-mariadb")]
        public void AcceptsMissingOrValidServerVersion(string serverVersion)
        {
            Assert.False(HasServerVersionFailure(serverVersion));
        }

        [Theory]
        [InlineData("latest")]
        [InlineData("mysql-8")]
        public void RejectsUnparsableServerVersion(string serverVersion)
        {
            Assert.True(HasServerVersionFailure(serverVersion));
        }
    }

    public class ValidateDatabaseJournalMode
    {
        private static bool HasJournalModeFailure(string journalMode)
        {
            var options = new PaGettoOptions
            {
                Database = new DatabaseOptions { Type = "Sqlite", JournalMode = journalMode },
            };
            var result = new ValidatePaGettoOptions().Validate(null, options);
            return result.Failed
                && result.Failures.Any(f => f.Contains($"{nameof(PaGettoOptions.Database)}:{nameof(DatabaseOptions.JournalMode)}"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("DELETE")]
        [InlineData("TRUNCATE")]
        [InlineData("PERSIST")]
        [InlineData("MEMORY")]
        [InlineData("WAL")]
        [InlineData("OFF")]
        [InlineData("wal")] // case-insensitive
        public void AcceptsMissingOrValidJournalMode(string journalMode)
        {
            Assert.False(HasJournalModeFailure(journalMode));
        }

        [Theory]
        [InlineData("WAL2")]
        [InlineData("WAL; DROP TABLE Packages")]
        public void RejectsUnknownJournalMode(string journalMode)
        {
            Assert.True(HasJournalModeFailure(journalMode));
        }
    }
}
