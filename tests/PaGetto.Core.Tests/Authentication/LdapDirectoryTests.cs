using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PaGetto.Core.Authentication.Ldap;
using PaGetto.Core.Configuration;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Authentication;

/// <summary>
/// Runs against the test LDAP server with the Planet Express users (see <see cref="LdapFactAttribute"/>):
/// <c>docker run -d -p 10389:10389 ghcr.io/rroemhild/docker-test-openldap:master</c> and
/// <c>PAGETTO_TEST_LDAP=localhost:10389</c>. Every user's password is its uid.
/// </summary>
public class LdapDirectoryTests
{
    public class AuthenticateAsync : FactsBase
    {
        [LdapFact]
        public async Task ReturnsTheUserForTheRightPassword()
        {
            var user = await Target.AuthenticateAsync("fry", "fry", Ct);

            Assert.NotNull(user);
            Assert.Equal("fry", user.Username);
            Assert.Equal("cn=Philip J. Fry,ou=people,dc=planetexpress,dc=com", user.Dn);
            Assert.True(Guid.TryParse(user.Id, out _));
            Assert.Equal("Fry", user.DisplayName);
            Assert.Equal("fry@planetexpress.com", user.Email);
            Assert.Equal(["ship_crew", "cn=ship_crew,ou=people,dc=planetexpress,dc=com"], user.Groups);
        }

        [LdapFact]
        public async Task FindsTheUserCaseInsensitively()
        {
            var user = await Target.AuthenticateAsync("FRY", "fry", Ct);

            Assert.Equal("fry", user?.Username);
        }

        [LdapFact]
        public async Task ReturnsNullForAWrongPassword()
        {
            Assert.Null(await Target.AuthenticateAsync("fry", "bender", Ct));
        }

        [LdapFact]
        public async Task ReturnsNullForAnEmptyPassword()
        {
            // An empty password would be an anonymous bind, which the server accepts.
            Assert.Null(await Target.AuthenticateAsync("fry", "", Ct));
        }

        [LdapFact]
        public async Task ReturnsNullForAnUnknownUser()
        {
            Assert.Null(await Target.AuthenticateAsync("nibbler", "nibbler", Ct));
        }

        [LdapFact]
        public async Task DoesNotLetTheUsernameChangeTheFilter()
        {
            // Unescaped, (uid=f*) would find fry.
            Assert.Null(await Target.AuthenticateAsync("f*", "fry", Ct));
        }

        [LdapFact]
        public async Task FindsTheGroupsWithTheGroupSearchFilter()
        {
            Options.Ldap.GroupAttribute = null;
            // The groups of the test data have the object class Group rather than groupOfNames.
            Options.Ldap.GroupSearchFilter = "(&(objectClass=Group)(member={0}))";

            var user = await Target.AuthenticateAsync("hermes", "hermes", Ct);

            Assert.Equal(["admin_staff", "cn=admin_staff,ou=people,dc=planetexpress,dc=com"], user.Groups);
        }

        [LdapFact]
        public async Task UsesTheCommonNameWithoutADisplayName()
        {
            Options.Ldap.DisplayNameAttribute = "givenName2";

            var user = await Target.AuthenticateAsync("fry", "fry", Ct);

            Assert.Equal("Philip J. Fry", user.DisplayName);
        }

        [LdapFact]
        public async Task ReturnsNoGroupsForAUserWithoutGroups()
        {
            var user = await Target.AuthenticateAsync("zoidberg", "zoidberg", Ct);

            Assert.Empty(user.Groups);
        }

        [LdapFact]
        public async Task ThrowsWhenTheSearchAccountIsRefused()
        {
            Options.Ldap.BindPassword = "wrong";

            await Assert.ThrowsAnyAsync<Exception>(() => Target.AuthenticateAsync("fry", "fry", Ct));
        }
    }

    public class EscapeFilterValue
    {
        [Theory]
        [InlineData("fry", "fry")]
        [InlineData("f*", "f\\2a")]
        [InlineData("a(b)c", "a\\28b\\29c")]
        [InlineData("back\\slash", "back\\5cslash")]
        [InlineData("nul\0", "nul\\00")]
        public void EscapesTheSpecialCharacters(string value, string expected)
        {
            Assert.Equal(expected, LdapDirectory.EscapeFilterValue(value));
        }
    }

    public class GetGroupNames
    {
        [Fact]
        public void ReturnsTheCommonNameAndTheDnOfEachGroup()
        {
            var names = LdapDirectory.GetGroupNames(
            [
                "CN=Package Admins,OU=Groups,DC=corp,DC=example",
                "cn=a\\,b,ou=groups,dc=example",
            ]);

            Assert.Equal(
                ["Package Admins", "CN=Package Admins,OU=Groups,DC=corp,DC=example", "a,b", "cn=a\\,b,ou=groups,dc=example"],
                names);
        }
    }

    public class FactsBase
    {
        protected readonly NugetAuthenticationOptions Options;
        protected readonly LdapDirectory Target;
        protected readonly CancellationToken Ct = CancellationToken.None;

        protected FactsBase()
        {
            var server = Environment.GetEnvironmentVariable(LdapFactAttribute.ServerVariable) ?? "localhost:10389";
            var separator = server.LastIndexOf(':');

            Options = new NugetAuthenticationOptions
            {
                Mode = AuthenticationMode.Local,
                Ldap = new LdapOptions
                {
                    Server = server[..separator],
                    Port = int.Parse(server[(separator + 1)..], CultureInfo.InvariantCulture),
                    Security = LdapSecurity.None,
                    BindDn = "cn=admin,dc=planetexpress,dc=com",
                    BindPassword = "GoodNewsEveryone",
                    SearchBase = "ou=people,dc=planetexpress,dc=com",
                },
            };

            var monitor = new Mock<IOptionsMonitor<NugetAuthenticationOptions>>();
            monitor.Setup(m => m.CurrentValue).Returns(Options);
            Target = new LdapDirectory(monitor.Object, Mock.Of<ILogger<LdapDirectory>>());
        }
    }
}
