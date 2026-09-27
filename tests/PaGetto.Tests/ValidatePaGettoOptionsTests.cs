using System.Collections.Generic;
using System.Linq;
using PaGetto.Core.Configuration;
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
