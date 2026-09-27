using System;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Notifications;
using Microsoft.Extensions.Options;
using Xunit;

namespace PaGetto.Core.Tests.Notifications;

public class PatExpiryEmailBuilderTests
{
    public class Build
    {
        private static PersonalAccessToken Token() => new()
        {
            Id = Guid.NewGuid(),
            Name = "ci-token",
            TokenPrefix = "bg_00000",
            ExpiresAtUtc = new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc),
            User = new User { Email = "owner@test.com" },
        };

        private static PatExpiryEmailBuilder BuildBuilder(string webBaseUrl = null)
        {
            return new PatExpiryEmailBuilder(
                Options.Create(new PaGettoOptions { PublicBaseUrl = webBaseUrl }),
                Options.Create(new PatExpiryNotificationOptions()));
        }

        [Fact]
        public void AddressesTheTokenOwnerAndDescribesTheDeadline()
        {
            var message = BuildBuilder().Build(Token(), daysUntilExpiry: 7);

            Assert.Equal(["owner@test.com"], message.To);
            Assert.Contains("in 7 days", message.Subject);
            Assert.Contains("ci-token", message.Subject);
        }

        [Theory]
        [InlineData(0, "today")]
        [InlineData(1, "in 1 day")]
        [InlineData(5, "in 5 days")]
        public void UsesFriendlyDeadlineWording(int daysUntil, string expected)
        {
            var message = BuildBuilder().Build(Token(), daysUntil);

            Assert.Contains(expected, message.Subject);
        }

        [Fact]
        public void IncludesTokenPageLinkWhenBaseUrlConfigured()
        {
            var message = BuildBuilder("https://packages.example.com/").Build(Token(), daysUntilExpiry: 2);

            Assert.Contains("""<a href="https://packages.example.com/account/tokens">""", message.Body);
        }

        [Fact]
        public void FallsBackToTheLegacyWebBaseUrl()
        {
#pragma warning disable CS0618 // The legacy setting is still honored.
            var builder = new PatExpiryEmailBuilder(
                Options.Create(new PaGettoOptions()),
                Options.Create(new PatExpiryNotificationOptions { WebBaseUrl = "https://old.example.com" }));
#pragma warning restore CS0618

            var message = builder.Build(Token(), daysUntilExpiry: 2);

            Assert.Contains("""<a href="https://old.example.com/account/tokens">""", message.Body);
        }

        [Fact]
        public void IncludesTokenPageLinkWhenBaseUrlHasNoTrailingSlash()
        {
            var message = BuildBuilder("https://packages.example.com").Build(Token(), daysUntilExpiry: 2);

            Assert.Contains("""<a href="https://packages.example.com/account/tokens">""", message.Body);
        }

        [Fact]
        public void ThrowsWhenTokenHasNoUser()
        {
            var token = Token();
            token.User = null;

            Assert.Throws<ArgumentException>(() => BuildBuilder().Build(token, daysUntilExpiry: 2));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ThrowsWhenTokenOwnerHasNoEmail(string email)
        {
            var token = Token();
            token.User.Email = email;

            Assert.Throws<ArgumentException>(() => BuildBuilder().Build(token, daysUntilExpiry: 2));
        }

        [Fact]
        public void OmitsLinkWhenBaseUrlNotConfigured()
        {
            var message = BuildBuilder().Build(Token(), daysUntilExpiry: 2);

            Assert.DoesNotContain("<a href", message.Body);
            Assert.Contains("Tokens page", message.Body);
        }
    }
}
