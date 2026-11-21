using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PaGetto.Core.Authentication;
using PaGetto.Core.Authentication.Ldap;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Authentication;

public class LdapAuthenticationServiceTests
{
    public class AuthenticateAsync : FactsBase
    {
        [Fact]
        public async Task CreatesTheAccountOfANewDirectoryUser()
        {
            var user = await Target.AuthenticateAsync("Fry", Password, Ct);

            Assert.NotNull(user);
            var stored = await Context.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id, Ct);
            Assert.Equal("fry", stored.Username);
            Assert.Equal("Philip J. Fry", stored.DisplayName);
            Assert.Equal("fry@planetexpress.com", stored.Email);
            Assert.Equal(AuthProvider.Ldap, stored.AuthProvider);
            Assert.Equal("uuid-fry", stored.EntraObjectId);
            Assert.True(stored.CanLoginToUI);
            Assert.False(stored.IsAdmin);
        }

        [Fact]
        public async Task SyncsTheRoleLinkedGroupsFromTheDirectoryGroups()
        {
            var crew = await Groups.CreateGroupAsync("Crew", "ship_crew", null, Ct);
            var staff = await Groups.CreateGroupAsync("Staff", "cn=admin_staff,ou=people,dc=planetexpress,dc=com", null, Ct);
            var manual = await Groups.CreateGroupAsync("Manual", null, null, Ct);

            var user = await Target.AuthenticateAsync("fry", Password, Ct);

            var groupIds = (await Groups.GetUserGroupsAsync(user.Id, Ct)).Select(g => g.Id).ToList();
            Assert.Contains(crew.Id, groupIds);
            Assert.DoesNotContain(staff.Id, groupIds);
            Assert.DoesNotContain(manual.Id, groupIds);
        }

        [Fact]
        public async Task MatchesGroupsByTheirDn()
        {
            var staff = await Groups.CreateGroupAsync("Staff", "cn=admin_staff,ou=people,dc=planetexpress,dc=com", null, Ct);
            Directory.Setup(d => d.AuthenticateAsync("hermes", Password, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Entry("hermes", "admin_staff", "cn=admin_staff,ou=people,dc=planetexpress,dc=com"));

            var user = await Target.AuthenticateAsync("hermes", Password, Ct);

            Assert.Contains(staff.Id, (await Groups.GetUserGroupsAsync(user.Id, Ct)).Select(g => g.Id));
        }

        [Fact]
        public async Task MakesMembersOfTheAdminGroupAdministrators()
        {
            Directory.Setup(d => d.AuthenticateAsync("professor", Password, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Entry("professor", "Admin", "cn=Admin,ou=groups,dc=planetexpress,dc=com"));

            var user = await Target.AuthenticateAsync("professor", Password, Ct);

            Assert.True(user.IsAdmin);
            Assert.True((await Context.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id, Ct)).IsAdmin);
        }

        [Fact]
        public async Task UpdatesTheAccountOnTheNextSignIn()
        {
            var first = await Target.AuthenticateAsync("fry", Password, Ct);
            Directory.Setup(d => d.AuthenticateAsync("fry", "NewPassword123!", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Entry("fry") with { DisplayName = "Fry", Email = "pjfry@planetexpress.com" });

            var second = await Target.AuthenticateAsync("fry", "NewPassword123!", Ct);

            Assert.Equal(first.Id, second.Id);
            var stored = await Context.Users.AsNoTracking().SingleAsync(u => u.Id == first.Id, Ct);
            Assert.Equal("Fry", stored.DisplayName);
            Assert.Equal("pjfry@planetexpress.com", stored.Email);
            Assert.Equal(1, await Context.Users.CountAsync(u => u.AuthProvider == AuthProvider.Ldap, Ct));
        }

        [Fact]
        public async Task RemembersASuccessfulSignIn()
        {
            var first = await Target.AuthenticateAsync("fry", Password, Ct);
            var second = await Target.AuthenticateAsync("FRY", Password, Ct);

            Assert.Equal(first.Id, second.Id);
            Directory.Verify(d => d.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DoesNotRememberAnotherPassword()
        {
            await Target.AuthenticateAsync("fry", Password, Ct);

            Assert.Null(await Target.AuthenticateAsync("fry", "WrongPassword123!", Ct));
            Directory.Verify(d => d.AuthenticateAsync("fry", "WrongPassword123!", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AsksTheDirectoryEveryTimeWithoutCache()
        {
            Options.Ldap.CacheSeconds = 0;

            await Target.AuthenticateAsync("fry", Password, Ct);
            await Target.AuthenticateAsync("fry", Password, Ct);

            Directory.Verify(d => d.AuthenticateAsync("fry", Password, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ReturnsNullForAWrongPassword()
        {
            Assert.Null(await Target.AuthenticateAsync("fry", "WrongPassword123!", Ct));
            Assert.Equal(0, await Context.Users.CountAsync(u => u.AuthProvider == AuthProvider.Ldap, Ct));
        }

        [Fact]
        public async Task ReturnsNullWhenTheDirectoryFails()
        {
            Directory.Setup(d => d.AuthenticateAsync("fry", Password, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Connection refused"));

            Assert.Null(await Target.AuthenticateAsync("fry", Password, Ct));
        }

        [Fact]
        public async Task RefusesANewUserWhoseUsernameIsTaken()
        {
            // The local account fry must not be taken over by the directory user fry.
            await Users.CreateLocalUserAsync("fry", "Local Fry", null, "LocalPassword123!", true, null, Ct);

            Assert.Null(await Target.AuthenticateAsync("fry", Password, Ct));
            Assert.Equal(0, await Context.Users.CountAsync(u => u.AuthProvider == AuthProvider.Ldap, Ct));
        }

        [Fact]
        public async Task ReturnsADisabledAccount()
        {
            var user = await Target.AuthenticateAsync("fry", Password, Ct);
            await Users.SetEnabledAsync(user.Id, false, Ct);

            var disabled = await Target.AuthenticateAsync("fry", Password, Ct);

            Assert.False(disabled.IsEnabled);
        }

        [Theory]
        [InlineData(AuthenticationMode.Entra)]
        [InlineData(AuthenticationMode.Oidc)]
        [InlineData(AuthenticationMode.Legacy)]
        public async Task IsOffInModesWithoutLocalAccounts(AuthenticationMode mode)
        {
            Options.Mode = mode;

            Assert.Null(await Target.AuthenticateAsync("fry", Password, Ct));
            Directory.VerifyNoOtherCalls();
        }
    }

    public class FactsBase : IDisposable
    {
        protected const string Password = "fry";

        protected readonly TestDbContext Context;
        protected readonly UserService Users;
        protected readonly GroupService Groups;
        protected readonly Mock<ILdapDirectory> Directory = new();
        protected readonly NugetAuthenticationOptions Options;
        protected readonly LdapAuthenticationService Target;
        protected readonly CancellationToken Ct = CancellationToken.None;

        private readonly LdapSignInCache _cache = new();

        protected FactsBase()
        {
            Context = TestDbContext.Create();
            Options = new NugetAuthenticationOptions { Mode = AuthenticationMode.Local, Ldap = new LdapOptions() };
            var snapshot = new Mock<IOptionsSnapshot<NugetAuthenticationOptions>>();
            snapshot.Setup(o => o.Value).Returns(Options);

            Users = new UserService(Context, snapshot.Object, Mock.Of<ILogger<UserService>>());
            Groups = new GroupService(Context, Mock.Of<ILogger<GroupService>>());
            var accounts = new ExternalAccountService(Users, Groups, Mock.Of<ILogger<ExternalAccountService>>());

            Directory.Setup(d => d.AuthenticateAsync(It.Is<string>(u => u.Equals("fry", StringComparison.OrdinalIgnoreCase)), Password, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Entry("fry", "ship_crew", "cn=ship_crew,ou=people,dc=planetexpress,dc=com"));

            Target = new LdapAuthenticationService(
                Directory.Object,
                _cache,
                accounts,
                Users,
                snapshot.Object,
                Mock.Of<ILogger<LdapAuthenticationService>>());
        }

        protected static LdapUser Entry(string uid, params string[] groups)
        {
            var displayName = uid == "fry" ? "Philip J. Fry" : uid;
            return new LdapUser($"uuid-{uid}", $"uid={uid},ou=people,dc=planetexpress,dc=com", uid, displayName, $"{uid}@planetexpress.com", groups);
        }

        public void Dispose()
        {
            _cache.Dispose();
            Context.Dispose();
        }
    }
}
