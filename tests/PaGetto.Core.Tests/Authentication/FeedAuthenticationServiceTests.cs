using System;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Authentication.Ldap;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace PaGetto.Core.Tests.Authentication;

public class FeedAuthenticationServiceTests
{
    public class AuthenticateByCredentialsAsync : FactsBase
    {
        [Theory]
        [InlineData(AuthenticationMode.Local)]
        [InlineData(AuthenticationMode.Hybrid)]
        public async Task AcceptsLocalPassword(AuthenticationMode mode)
        {
            var target = CreateTarget(mode);
            var user = await CreateLocalUser("alice");

            var result = await target.AuthenticateByCredentialsAsync("alice", Password, Ct);

            Assert.True(result.IsAuthenticated);
            Assert.Equal(user.Id, result.UserId);
        }

        [Theory]
        [InlineData(AuthenticationMode.Local)]
        [InlineData(AuthenticationMode.Entra)]
        [InlineData(AuthenticationMode.Hybrid)]
        public async Task AcceptsTokenAsPassword(AuthenticationMode mode)
        {
            var target = CreateTarget(mode);
            var user = await CreateLocalUser("alice");
            var token = await CreateToken(user.Id);

            var result = await target.AuthenticateByCredentialsAsync("alice", token, Ct);

            Assert.True(result.IsAuthenticated);
            Assert.Equal(user.Id, result.UserId);
        }

        [Fact]
        public async Task RejectsTokenOfAnotherUser()
        {
            var target = CreateTarget(AuthenticationMode.Local);
            await CreateLocalUser("alice");
            var bob = await CreateLocalUser("bob");
            var token = await CreateToken(bob.Id);

            var result = await target.AuthenticateByCredentialsAsync("alice", token, Ct);

            Assert.False(result.IsAuthenticated);
        }

        [Fact]
        public async Task RecordsTheTokenThatLimitsTheRequest()
        {
            var target = CreateTarget(AuthenticationMode.Local);
            var user = await CreateLocalUser("alice");
            var created = await Tokens.CreateTokenAsync(user.Id, "ci", DateTime.UtcNow.AddDays(30), TokenPermissions.ReadOnly, Ct);

            var result = await target.AuthenticateByCredentialsAsync("alice", created.PlaintextToken, Ct);

            Assert.True(result.IsAuthenticated);
            Assert.Equal(created.Token.Id, TokenScope.Token?.Id);
            Assert.False(TokenScope.AllowsPush(user.Id, Guid.NewGuid()));
        }

        [Fact]
        public async Task RejectsRevokedToken()
        {
            var target = CreateTarget(AuthenticationMode.Local);
            var user = await CreateLocalUser("alice");
            var created = await Tokens.CreateTokenAsync(user.Id, "ci", DateTime.UtcNow.AddDays(30), Ct);
            await Tokens.RevokeTokenAsync(created.Token.Id, Ct);

            var result = await target.AuthenticateByCredentialsAsync("alice", created.PlaintextToken, Ct);

            Assert.False(result.IsAuthenticated);
        }

        [Fact]
        public async Task RejectsExpiredToken()
        {
            var target = CreateTarget(AuthenticationMode.Local);
            var user = await CreateLocalUser("alice");
            var token = await CreateToken(user.Id);
            var stored = await Context.PersonalAccessTokens.SingleAsync(Ct);
            stored.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await Context.SaveChangesAsync(Ct);

            var result = await target.AuthenticateByCredentialsAsync("alice", token, Ct);

            Assert.False(result.IsAuthenticated);
        }

        [Theory]
        [InlineData(AuthenticationMode.Local)]
        [InlineData(AuthenticationMode.Hybrid)]
        public async Task TokenAttemptsDoNotCountAsFailedLogins(AuthenticationMode mode)
        {
            var target = CreateTarget(mode);
            var user = await CreateLocalUser("alice");
            var token = await CreateToken(user.Id);

            for (var i = 0; i < MaxFailedAttempts + 1; i++)
            {
                Assert.True((await target.AuthenticateByCredentialsAsync("alice", token, Ct)).IsAuthenticated);
                Assert.False((await target.AuthenticateByCredentialsAsync("alice", "bg_not-a-real-token", Ct)).IsAuthenticated);
            }

            var reloaded = await Context.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id, Ct);
            Assert.Equal(0, reloaded.FailedLoginCount);
            Assert.Null(reloaded.LockedUntilUtc);
            Assert.True((await target.AuthenticateByCredentialsAsync("alice", Password, Ct)).IsAuthenticated);
        }

        [Fact]
        public async Task WrongPasswordCountsAsFailedLogin()
        {
            var target = CreateTarget(AuthenticationMode.Local);
            var user = await CreateLocalUser("alice");

            var result = await target.AuthenticateByCredentialsAsync("alice", "WrongPassword123!", Ct);

            Assert.False(result.IsAuthenticated);
            var reloaded = await Context.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id, Ct);
            Assert.Equal(1, reloaded.FailedLoginCount);
        }

        [Fact]
        public async Task RejectsPasswordThatMustBeChanged()
        {
            var target = CreateTarget(AuthenticationMode.Local);
            await Users.CreateLocalAdminAsync("root", Password, mustChangePassword: true, Ct);

            var result = await target.AuthenticateByCredentialsAsync("root", Password, Ct);

            Assert.False(result.IsAuthenticated);
        }

        [Theory]
        [InlineData(AuthenticationMode.Local)]
        [InlineData(AuthenticationMode.Hybrid)]
        public async Task AcceptsDirectoryPasswordOfUnknownUsername(AuthenticationMode mode)
        {
            var target = CreateTarget(mode, ldap: true);
            var user = await CreateLdapUser("fry");
            Ldap.Setup(l => l.AuthenticateAsync("Fry", Password, Ct)).ReturnsAsync(user);

            var result = await target.AuthenticateByCredentialsAsync("Fry", Password, Ct);

            Assert.True(result.IsAuthenticated);
            Assert.Equal(user.Id, result.UserId);
            Assert.Equal("fry", result.Username);
        }

        [Fact]
        public async Task RejectsWrongDirectoryPassword()
        {
            var target = CreateTarget(AuthenticationMode.Local, ldap: true);
            await CreateLdapUser("fry");

            var result = await target.AuthenticateByCredentialsAsync("fry", "WrongPassword123!", Ct);

            Assert.False(result.IsAuthenticated);
            Ldap.Verify(l => l.AuthenticateAsync("fry", "WrongPassword123!", Ct));
        }

        [Fact]
        public async Task RejectsDisabledDirectoryUser()
        {
            var target = CreateTarget(AuthenticationMode.Local, ldap: true);
            var user = await CreateLdapUser("fry");
            user.IsEnabled = false;
            Ldap.Setup(l => l.AuthenticateAsync("fry", Password, Ct)).ReturnsAsync(user);

            var result = await target.AuthenticateByCredentialsAsync("fry", Password, Ct);

            Assert.False(result.IsAuthenticated);
        }

        [Fact]
        public async Task ChecksLocalAccountsOnlyLocally()
        {
            // A directory user with the name of a local account must not get into that account,
            // and its password attempts must not reach the directory.
            var target = CreateTarget(AuthenticationMode.Local, ldap: true);
            var user = await CreateLocalUser("alice");

            Assert.False((await target.AuthenticateByCredentialsAsync("alice", "DirectoryPassword1!", Ct)).IsAuthenticated);
            Assert.Equal(user.Id, (await target.AuthenticateByCredentialsAsync("alice", Password, Ct)).UserId);
            Ldap.Verify(l => l.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task IgnoresTheDirectoryWithoutLdapConfig()
        {
            var target = CreateTarget(AuthenticationMode.Local);

            var result = await target.AuthenticateByCredentialsAsync("fry", Password, Ct);

            Assert.False(result.IsAuthenticated);
            Ldap.Verify(l => l.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task EntraModeRejectsLocalPassword()
        {
            var target = CreateTarget(AuthenticationMode.Entra);
            await CreateLocalUser("alice");

            var result = await target.AuthenticateByCredentialsAsync("alice", Password, Ct);

            Assert.False(result.IsAuthenticated);
        }
    }

    public class FactsBase : IDisposable
    {
        protected const string Password = "TestPassword123!";
        protected const int MaxFailedAttempts = 3;

        protected readonly TestDbContext Context;
        protected readonly UserService Users;
        protected readonly TokenService Tokens;
        protected readonly CancellationToken Ct = CancellationToken.None;

        private readonly Guid _adminUserId;

        protected FactsBase()
        {
            Context = TestDbContext.Create();

            _adminUserId = Guid.NewGuid();
            Context.Users.Add(new User
            {
                Id = _adminUserId,
                Username = "admin",
                DisplayName = "Admin",
                AuthProvider = AuthProvider.Entra,
                EntraObjectId = "oid-admin",
                IsEnabled = true,
                CanLoginToUI = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            Context.SaveChanges();

            var options = CreateOptions(AuthenticationMode.Local);
            Users = new UserService(Context, options, Mock.Of<ILogger<UserService>>());
            Tokens = new TokenService(Context, options, Mock.Of<ILogger<TokenService>>());
        }

        protected RequestTokenScope TokenScope { get; } = new();

        protected Mock<ILdapAuthenticationService> Ldap { get; } = new();

        protected FeedAuthenticationService CreateTarget(AuthenticationMode mode, bool ldap = false)
        {
            return new FeedAuthenticationService(
                Users,
                Tokens,
                TokenScope,
                Ldap.Object,
                CreateOptions(mode, ldap),
                Mock.Of<ILogger<FeedAuthenticationService>>());
        }

        protected async Task<User> CreateLocalUser(string username)
        {
            return await Users.CreateLocalUserAsync(
                username, username, $"{username}@test.com", Password, true, _adminUserId, Ct);
        }

        protected async Task<User> CreateLdapUser(string username)
        {
            return await Users.CreateExternalUserAsync(
                AuthProvider.Ldap, $"uuid-{username}", username, username, $"{username}@test.com", Ct);
        }

        protected async Task<string> CreateToken(Guid userId)
        {
            var result = await Tokens.CreateTokenAsync(userId, "ci", DateTime.UtcNow.AddDays(30), Ct);
            return result.PlaintextToken;
        }

        private static IOptionsSnapshot<NugetAuthenticationOptions> CreateOptions(AuthenticationMode mode, bool ldap = false)
        {
            var options = new Mock<IOptionsSnapshot<NugetAuthenticationOptions>>();
            options.Setup(o => o.Value).Returns(new NugetAuthenticationOptions
            {
                Mode = mode,
                Ldap = ldap ? new LdapOptions() : null,
                MaxFailedAttempts = MaxFailedAttempts,
                LockoutMinutes = 15,
                MaxTokenExpiryDays = 365
            });
            return options.Object;
        }

        public void Dispose()
        {
            Context?.Dispose();
        }
    }
}
