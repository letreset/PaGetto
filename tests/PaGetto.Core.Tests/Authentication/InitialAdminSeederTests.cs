using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace PaGetto.Core.Tests.Authentication;

public class InitialAdminSeederTests
{
    public class SeedAsync : FactsBase
    {
        [Theory]
        [InlineData(AuthenticationMode.Local)]
        [InlineData(AuthenticationMode.Hybrid)]
        public async Task CreatesDefaultAdminWhenNoAdminExists(AuthenticationMode mode)
        {
            await CreateTarget(mode).SeedAsync(Ct);

            var user = Assert.Single(Context.Users.ToList());
            Assert.Equal(InitialAdminSeeder.DefaultUsername, user.Username);
            Assert.True(user.IsAdmin);
            Assert.True(user.IsEnabled);
            Assert.True(user.CanLoginToUI);
            Assert.True(user.MustChangePassword);
            Assert.Equal(AuthProvider.Local, user.AuthProvider);
            Assert.True(await Users.VerifyPasswordAsync(user, InitialAdminSeeder.DefaultPassword));
            VerifyLogged(LogLevel.Warning, Times.Once());
        }

        [Fact]
        public async Task DoesNothingWhenAnAdminExists()
        {
            AddUser("existing-admin", isAdmin: true);

            await CreateTarget(AuthenticationMode.Local).SeedAsync(Ct);

            var user = Assert.Single(Context.Users.ToList());
            Assert.Equal("existing-admin", user.Username);
            VerifyLogged(LogLevel.Warning, Times.Never());
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public async Task CreatesAdminWhenNoAdminCanSignIn(bool isEnabled, bool canLoginToUI)
        {
            AddUser("locked-out-admin", isAdmin: true, isEnabled, canLoginToUI);

            await CreateTarget(AuthenticationMode.Local).SeedAsync(Ct);

            var created = Assert.Single(
                Context.Users.AsNoTracking().ToList(), u => u.Username == InitialAdminSeeder.DefaultUsername);
            Assert.True(created.IsAdmin);
        }

        [Fact]
        public async Task DoesNotPromoteAnExistingNonAdminUser()
        {
            var existing = AddUser(InitialAdminSeeder.DefaultUsername, isAdmin: false);

            await CreateTarget(AuthenticationMode.Local).SeedAsync(Ct);

            var user = Assert.Single(Context.Users.AsNoTracking().ToList());
            Assert.Equal(existing.Id, user.Id);
            Assert.False(user.IsAdmin);
            Assert.Null(user.PasswordHash);
            VerifyLogged(LogLevel.Warning, Times.Once());
        }

        [Theory]
        [InlineData(AuthenticationMode.Legacy)]
        [InlineData(AuthenticationMode.Entra)]
        public async Task DoesNothingInModesWithoutLocalAccounts(AuthenticationMode mode)
        {
            await CreateTarget(mode).SeedAsync(Ct);

            Assert.Empty(Context.Users.ToList());
            VerifyLogged(LogLevel.Warning, Times.Never());
        }

        [Fact]
        public async Task TreatsADuplicateInsertAsAlreadySeeded()
        {
            // Another replica inserted the same username between our checks and our insert.
            var users = new Mock<IUserService>();
            users
                .Setup(u => u.CreateLocalAdminAsync(
                    InitialAdminSeeder.DefaultUsername, InitialAdminSeeder.DefaultPassword, true, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateException("duplicate", new SqliteException("UNIQUE constraint failed", 19)));

            var target = CreateTarget(AuthenticationMode.Local, users.Object);

            await target.SeedAsync(Ct);

            users.Verify(u => u.CreateLocalAdminAsync(
                InitialAdminSeeder.DefaultUsername, InitialAdminSeeder.DefaultPassword, true, It.IsAny<CancellationToken>()), Times.Once);
            VerifyLogged(LogLevel.Warning, Times.Never());
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly TestDbContext Context;
        protected readonly UserService Users;
        protected readonly Mock<ILogger<InitialAdminSeeder>> Logger = new();
        protected readonly CancellationToken Ct = CancellationToken.None;

        protected FactsBase()
        {
            Context = TestDbContext.Create();
            Logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            Users = new UserService(
                Context,
                Snapshot(new NugetAuthenticationOptions()),
                Mock.Of<ILogger<UserService>>());
        }

        protected InitialAdminSeeder CreateTarget(AuthenticationMode mode, IUserService userService = null)
        {
            var options = new NugetAuthenticationOptions { Mode = mode };

            return new InitialAdminSeeder(Context, userService ?? Users, Snapshot(options), Logger.Object);
        }

        protected User AddUser(string username, bool isAdmin, bool isEnabled = true, bool canLoginToUI = true)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                DisplayName = username,
                AuthProvider = AuthProvider.Entra,
                EntraObjectId = $"oid-{username}",
                IsEnabled = isEnabled,
                CanLoginToUI = canLoginToUI,
                IsAdmin = isAdmin,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            Context.Users.Add(user);
            Context.SaveChanges();
            return user;
        }

        protected void VerifyLogged(LogLevel level, Times times)
        {
            Logger.Verify(
                l => l.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                times);
        }

        private static IOptionsSnapshot<NugetAuthenticationOptions> Snapshot(NugetAuthenticationOptions options)
        {
            var snapshot = new Mock<IOptionsSnapshot<NugetAuthenticationOptions>>();
            snapshot.Setup(o => o.Value).Returns(options);
            return snapshot.Object;
        }

        public void Dispose()
        {
            Context?.Dispose();
        }
    }
}
