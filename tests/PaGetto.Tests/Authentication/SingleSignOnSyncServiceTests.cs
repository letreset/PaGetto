using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Authentication;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace PaGetto.Tests.Authentication;

public class SingleSignOnSyncServiceTests
{
    public class OnTokenValidatedAsync : FactsBase
    {
        [Fact]
        public async Task SkipsWhenObjectIdMissing()
        {
            var principal = CreatePrincipal(oid: null);

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct);

            UserService.Verify(
                s => s.FindByExternalIdAsync(It.IsAny<AuthProvider>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ProvisionsNewUser()
        {
            var principal = CreatePrincipal(oid: "oid-1", email: "alice@test.com", name: "Alice Test");
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Entra, "oid-1", Ct)).ReturnsAsync((User)null);
            UserService.Setup(s => s.CreateExternalUserAsync(AuthProvider.Entra, "oid-1", "alice@test.com", "Alice Test", "alice@test.com", Ct))
                .ReturnsAsync(CreateUserEntity("oid-1", "alice@test.com"));

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct);

            UserService.Verify(s => s.CreateExternalUserAsync(AuthProvider.Entra, "oid-1", "alice@test.com", "Alice Test", "alice@test.com", Ct));
        }

        [Fact]
        public async Task UsesRawEmailClaimWhenClaimTypeMappingDisabled()
        {
            // Microsoft.Identity.Web's JsonWebTokenHandler defaults MapInboundClaims to false, so
            // Entra ID tokens surface the raw "email" claim instead of the mapped ClaimTypes.Email.
            var principal = CreatePrincipal(oid: "oid-rawemail", rawEmail: "erin@test.com", name: "Erin Test");
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Entra, "oid-rawemail", Ct)).ReturnsAsync((User)null);
            UserService.Setup(s => s.CreateExternalUserAsync(AuthProvider.Entra, "oid-rawemail", "erin@test.com", "Erin Test", "erin@test.com", Ct))
                .ReturnsAsync(CreateUserEntity("oid-rawemail", "erin@test.com"));

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct);

            UserService.Verify(s => s.CreateExternalUserAsync(AuthProvider.Entra, "oid-rawemail", "erin@test.com", "Erin Test", "erin@test.com", Ct));
        }

        [Fact]
        public async Task StoresUpnAsEmailWhenNoMailClaimPresent()
        {
            // No email/mail claim, so the UPN is the best deliverable address available.
            var principal = CreatePrincipal(oid: "oid-upn", upn: "carol@test.com", preferredUsername: "carol@corp.local");
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Entra, "oid-upn", Ct)).ReturnsAsync((User)null);
            UserService.Setup(s => s.CreateExternalUserAsync(AuthProvider.Entra, "oid-upn", "carol@test.com", "carol@test.com", "carol@test.com", Ct))
                .ReturnsAsync(CreateUserEntity("oid-upn", "carol@test.com"));

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct);

            UserService.Verify(s => s.CreateExternalUserAsync(AuthProvider.Entra, "oid-upn", "carol@test.com", "carol@test.com", "carol@test.com", Ct));
        }

        [Fact]
        public async Task DoesNotUsePreferredUsernameAsEmail()
        {
            // preferred_username is often a non-deliverable UPN, so it must never be stored as the mailbox.
            var principal = CreatePrincipal(oid: "oid-pu", preferredUsername: "dave@corp.local");
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Entra, "oid-pu", Ct)).ReturnsAsync((User)null);
            UserService.Setup(s => s.CreateExternalUserAsync(AuthProvider.Entra, "oid-pu", "oid-pu", "oid-pu", null, Ct))
                .ReturnsAsync(CreateUserEntity("oid-pu", "oid-pu"));

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct);

            UserService.Verify(s => s.CreateExternalUserAsync(AuthProvider.Entra, "oid-pu", "oid-pu", "oid-pu", null, Ct));
        }

        [Fact]
        public async Task UpdatesDisplayNameForExistingUser()
        {
            var user = CreateUserEntity("oid-2", "bob@test.com", displayName: "Old Name");
            var principal = CreatePrincipal(oid: "oid-2", email: "bob@test.com", name: "New Name");
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Entra, "oid-2", Ct)).ReturnsAsync(user);

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct);

            Assert.Equal("New Name", user.DisplayName);
            UserService.Verify(s => s.UpdateUserAsync(user, Ct));
        }

        [Fact]
        public async Task ThrowsWhenUserDisabled()
        {
            var user = CreateUserEntity("oid-3", "disabled@test.com");
            user.IsEnabled = false;
            var principal = CreatePrincipal(oid: "oid-3", roles: new[] { "Admin" });
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Entra, "oid-3", Ct)).ReturnsAsync(user);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct));

            GroupService.Verify(
                s => s.SyncAppRoleMembershipsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ThrowsWhenCanLoginToUIDisabled()
        {
            var user = CreateUserEntity("oid-nologin", "nologin@test.com");
            user.IsEnabled = true;
            user.CanLoginToUI = false;
            var principal = CreatePrincipal(oid: "oid-nologin", roles: new[] { "Admin" });
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Entra, "oid-nologin", Ct)).ReturnsAsync(user);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct));

            GroupService.Verify(
                s => s.SyncAppRoleMembershipsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task GrantsAdminWhenAdminRolePresent()
        {
            var user = CreateUserEntity("oid-4", "admin@test.com");
            user.IsAdmin = false;
            var principal = CreatePrincipal(oid: "oid-4", roles: new[] { "Admin" });
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Entra, "oid-4", Ct)).ReturnsAsync(user);

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct);

            UserService.Verify(s => s.SetAdminAsync(user.Id, true, Ct));
        }

        [Fact]
        public async Task RevokesAdminWhenAdminRoleMissing()
        {
            var user = CreateUserEntity("oid-5", "wasadmin@test.com");
            user.IsAdmin = true;
            var principal = CreatePrincipal(oid: "oid-5", roles: new[] { "TeamFrontend" });
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Entra, "oid-5", Ct)).ReturnsAsync(user);

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct);

            UserService.Verify(s => s.SetAdminAsync(user.Id, false, Ct));
        }

        [Fact]
        public async Task DoesNotChangeAdminWhenAlreadyCorrect()
        {
            var user = CreateUserEntity("oid-6", "alreadyadmin@test.com");
            user.IsAdmin = true;
            var principal = CreatePrincipal(oid: "oid-6", roles: new[] { "Admin" });
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Entra, "oid-6", Ct)).ReturnsAsync(user);

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct);

            UserService.Verify(s => s.SetAdminAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SyncsGroupMembershipsFromRoles()
        {
            var user = CreateUserEntity("oid-7", "roleuser@test.com");
            var principal = CreatePrincipal(oid: "oid-7", roles: new[] { "TeamFrontend", "TeamBackend" });
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Entra, "oid-7", Ct)).ReturnsAsync(user);

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct);

            GroupService.Verify(s => s.SyncAppRoleMembershipsAsync(
                user.Id,
                It.Is<IReadOnlyList<string>>(r => r.Count == 2),
                Ct));
        }

        [Fact]
        public async Task SyncsEmptyRolesList()
        {
            var user = CreateUserEntity("oid-8", "noroles@test.com");
            var principal = CreatePrincipal(oid: "oid-8", roles: Array.Empty<string>());
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Entra, "oid-8", Ct)).ReturnsAsync(user);

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct);

            GroupService.Verify(s => s.SyncAppRoleMembershipsAsync(
                user.Id,
                It.Is<IReadOnlyList<string>>(r => r.Count == 0),
                Ct));
        }

        [Fact]
        public async Task ManualGroupsNotTouchedBySync()
        {
            var user = CreateUserEntity("oid-9", "manual@test.com");
            var principal = CreatePrincipal(oid: "oid-9", roles: new[] { "TeamQA" });
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Entra, "oid-9", Ct)).ReturnsAsync(user);

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct);

            // SyncAppRoleMembershipsAsync is called which internally only touches role-linked groups
            GroupService.Verify(s => s.SyncAppRoleMembershipsAsync(
                user.Id,
                It.Is<IReadOnlyList<string>>(r => r.Count == 1 && r[0] == "TeamQA"),
                Ct));
        }

        [Fact]
        public async Task ReplacesNameIdentifierClaimWithPaGettoId()
        {
            var user = CreateUserEntity("oid-10", "claims@test.com");
            var principal = CreatePrincipal(oid: "oid-10");
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Entra, "oid-10", Ct)).ReturnsAsync(user);

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct);

            var identity = principal.Identity as ClaimsIdentity;
            var nameIdClaim = identity?.FindFirst(ClaimTypes.NameIdentifier);
            Assert.NotNull(nameIdClaim);
            Assert.Equal(user.Id.ToString(), nameIdClaim.Value);

            var authProviderClaim = identity?.FindFirst("auth_provider");
            Assert.NotNull(authProviderClaim);
            Assert.Equal("Entra", authProviderClaim.Value);
        }

        [Fact]
        public async Task UsesCustomRoleClaim()
        {
            Options.Value.Authentication.Entra.RoleClaim = "custom_roles";
            var user = CreateUserEntity("oid-11", "custom@test.com");
            var principal = CreatePrincipal(oid: "oid-11", roles: new[] { "Admin" }, roleClaim: "custom_roles");
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Entra, "oid-11", Ct)).ReturnsAsync(user);
            user.IsAdmin = false;

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Entra, Ct);

            UserService.Verify(s => s.SetAdminAsync(user.Id, true, Ct));
        }

        [Fact]
        public async Task ProvisionsOidcUserFromSubjectAndPreferredUsername()
        {
            var principal = CreateOidcPrincipal(sub: "sub-1", preferredUsername: "alice", name: "Alice Test");
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Oidc, "sub-1", Ct)).ReturnsAsync((User)null);
            UserService.Setup(s => s.CreateExternalUserAsync(AuthProvider.Oidc, "sub-1", "alice", "Alice Test", null, Ct))
                .ReturnsAsync(CreateUserEntity("sub-1", "alice", provider: AuthProvider.Oidc));

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Oidc, Ct);

            UserService.Verify(s => s.CreateExternalUserAsync(AuthProvider.Oidc, "sub-1", "alice", "Alice Test", null, Ct));
        }

        [Fact]
        public async Task PrefersEmailAsOidcUsername()
        {
            var principal = CreateOidcPrincipal(sub: "sub-2", preferredUsername: "bob", email: "bob@test.com");
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Oidc, "sub-2", Ct)).ReturnsAsync((User)null);
            UserService.Setup(s => s.CreateExternalUserAsync(AuthProvider.Oidc, "sub-2", "bob@test.com", "bob@test.com", "bob@test.com", Ct))
                .ReturnsAsync(CreateUserEntity("sub-2", "bob@test.com", provider: AuthProvider.Oidc));

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Oidc, Ct);

            UserService.Verify(s => s.CreateExternalUserAsync(AuthProvider.Oidc, "sub-2", "bob@test.com", "bob@test.com", "bob@test.com", Ct));
        }

        [Fact]
        public async Task IgnoresTheEntraObjectIdForOidc()
        {
            // An OIDC user is identified by its subject only.
            var principal = CreateOidcPrincipal(sub: null, preferredUsername: "carol");
            principal.Identities.Single().AddClaim(new Claim("oid", "oid-1"));

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Oidc, Ct);

            UserService.Verify(
                s => s.FindByExternalIdAsync(It.IsAny<AuthProvider>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task SyncsGroupsAndAdminFromTheOidcRoleClaim()
        {
            var user = CreateUserEntity("sub-3", "dave", provider: AuthProvider.Oidc);
            var principal = CreateOidcPrincipal(sub: "sub-3", preferredUsername: "dave", groups: ["Admin", "developers"]);
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Oidc, "sub-3", Ct)).ReturnsAsync(user);

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Oidc, Ct);

            UserService.Verify(s => s.SetAdminAsync(user.Id, true, Ct));
            GroupService.Verify(s => s.SyncAppRoleMembershipsAsync(
                user.Id,
                It.Is<IReadOnlyList<string>>(r => r.Count == 2 && r.Contains("developers")),
                Ct));

            var identity = (ClaimsIdentity)principal.Identity;
            Assert.Equal(user.Id.ToString(), identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            Assert.Equal("Oidc", identity.FindFirst("auth_provider")?.Value);
        }

        [Fact]
        public async Task DeniesNewUserWhoseUsernameIsTaken()
        {
            var principal = CreateOidcPrincipal(sub: "sub-4", preferredUsername: "admin");
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Oidc, "sub-4", Ct)).ReturnsAsync((User)null);
            UserService.Setup(s => s.FindByUsernameAsync("admin", Ct))
                .ReturnsAsync(new User { Id = Guid.NewGuid(), Username = "admin", AuthProvider = AuthProvider.Local });

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => Target.OnTokenValidatedAsync(principal, AuthProvider.Oidc, Ct));

            UserService.Verify(
                s => s.CreateExternalUserAsync(It.IsAny<AuthProvider>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task KeepsTheUsernameWhenTheNewOneIsTaken()
        {
            var user = CreateUserEntity("sub-5", "erin", provider: AuthProvider.Oidc);
            var principal = CreateOidcPrincipal(sub: "sub-5", preferredUsername: "admin");
            UserService.Setup(s => s.FindByExternalIdAsync(AuthProvider.Oidc, "sub-5", Ct)).ReturnsAsync(user);
            UserService.Setup(s => s.FindByUsernameAsync("admin", Ct))
                .ReturnsAsync(new User { Id = Guid.NewGuid(), Username = "admin", AuthProvider = AuthProvider.Local });

            await Target.OnTokenValidatedAsync(principal, AuthProvider.Oidc, Ct);

            Assert.Equal("erin", user.Username);
        }
    }

    public class FactsBase
    {
        protected readonly Mock<IUserService> UserService;
        protected readonly Mock<IGroupService> GroupService;
        protected readonly IOptions<PaGettoOptions> Options;
        protected readonly SingleSignOnSyncService Target;
        protected readonly CancellationToken Ct = CancellationToken.None;

        protected FactsBase()
        {
            UserService = new Mock<IUserService>();
            GroupService = new Mock<IGroupService>();

            var opts = new PaGettoOptions
            {
                Authentication = new NugetAuthenticationOptions
                {
                    Entra = new EntraOptions
                    {
                        RoleClaim = "roles"
                    },
                    Oidc = new OidcOptions()
                }
            };
            Options = Microsoft.Extensions.Options.Options.Create(opts);

            Target = new SingleSignOnSyncService(
                UserService.Object,
                GroupService.Object,
                Options,
                Mock.Of<ILogger<SingleSignOnSyncService>>());
        }

        protected static User CreateUserEntity(
            string oid, string username, string displayName = null, AuthProvider provider = AuthProvider.Entra)
        {
            return new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                DisplayName = displayName ?? username,
                AuthProvider = provider,
                EntraObjectId = oid,
                IsEnabled = true,
                CanLoginToUI = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        // The claims of an ID token from an OpenID Connect provider such as Keycloak, unmapped.
        protected static ClaimsPrincipal CreateOidcPrincipal(
            string sub,
            string preferredUsername = null,
            string email = null,
            string name = null,
            string[] groups = null)
        {
            var claims = new List<Claim>();
            if (sub != null)
                claims.Add(new Claim("sub", sub));
            if (preferredUsername != null)
                claims.Add(new Claim("preferred_username", preferredUsername));
            if (email != null)
                claims.Add(new Claim("email", email));
            if (name != null)
                claims.Add(new Claim("name", name));
            foreach (var group in groups ?? [])
                claims.Add(new Claim("groups", group));

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        }

        protected static ClaimsPrincipal CreatePrincipal(
            string oid = null,
            string email = null,
            string name = null,
            string[] roles = null,
            string roleClaim = "roles",
            string preferredUsername = null,
            string upn = null,
            string rawEmail = null)
        {
            var claims = new List<Claim>();

            if (oid != null)
                claims.Add(new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", oid));

            if (email != null)
                claims.Add(new Claim(ClaimTypes.Email, email));

            if (rawEmail != null)
                claims.Add(new Claim("email", rawEmail));

            if (preferredUsername != null)
                claims.Add(new Claim("preferred_username", preferredUsername));

            if (upn != null)
                claims.Add(new Claim(ClaimTypes.Upn, upn));

            if (name != null)
                claims.Add(new Claim("name", name));

            if (roles != null)
            {
                foreach (var role in roles)
                    claims.Add(new Claim(roleClaim, role));
            }

            // Add a default NameIdentifier that should be replaced
            claims.Add(new Claim(ClaimTypes.NameIdentifier, "entra-sub-value"));

            var identity = new ClaimsIdentity(claims, "TestAuth");
            return new ClaimsPrincipal(identity);
        }
    }
}
