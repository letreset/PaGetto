using System;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
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

        protected FeedAuthenticationService CreateTarget(AuthenticationMode mode)
        {
            return new FeedAuthenticationService(
                Users,
                Tokens,
                CreateOptions(mode),
                Mock.Of<ILogger<FeedAuthenticationService>>());
        }

        protected async Task<User> CreateLocalUser(string username)
        {
            return await Users.CreateLocalUserAsync(
                username, username, $"{username}@test.com", Password, true, _adminUserId, Ct);
        }

        protected async Task<string> CreateToken(Guid userId)
        {
            var result = await Tokens.CreateTokenAsync(userId, "ci", DateTime.UtcNow.AddDays(30), Ct);
            return result.PlaintextToken;
        }

        private static IOptionsSnapshot<NugetAuthenticationOptions> CreateOptions(AuthenticationMode mode)
        {
            var options = new Mock<IOptionsSnapshot<NugetAuthenticationOptions>>();
            options.Setup(o => o.Value).Returns(new NugetAuthenticationOptions
            {
                Mode = mode,
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
