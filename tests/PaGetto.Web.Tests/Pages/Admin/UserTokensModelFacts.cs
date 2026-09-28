using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Audit;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Web.Pages.Admin;
using PaGetto.Web.Tests.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Moq;
using Xunit;

namespace PaGetto.Web.Tests.Pages.Admin;

public class UserTokensModelFacts
{
    public class OnGetAsync : FactsBase
    {
        [Fact]
        public async Task ListsTheUsersTokens()
        {
            var target = Build(_adminId);

            Assert.IsType<PageResult>(await target.OnGetAsync(_bob.Id, CancellationToken.None));

            Assert.Same(_bob, target.TargetUser);
            Assert.Equal([_active, _revoked, _expired], target.Tokens);
            Assert.True(target.IsActive(_active));
            Assert.False(target.IsActive(_revoked));
            Assert.False(target.IsActive(_expired));
        }

        [Fact]
        public async Task RedirectsNonAdmins()
        {
            var result = await Build(_bob.Id).OnGetAsync(_bob.Id, CancellationToken.None);

            Assert.Equal("/Index", Assert.IsType<RedirectToPageResult>(result).PageName);
            _tokens.Verify(t => t.GetUserTokensAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ReturnsNotFoundForAnUnknownUser()
        {
            Assert.IsType<NotFoundResult>(await Build(_adminId).OnGetAsync(Guid.NewGuid(), CancellationToken.None));
        }
    }

    public class OnPostRevokeAsync : FactsBase
    {
        [Fact]
        public async Task RevokesTheTokenAndAuditsIt()
        {
            var target = Build(_adminId);

            await target.OnPostRevokeAsync(_bob.Id, _active.Id, CancellationToken.None);

            _tokens.Verify(t => t.RevokeTokenAsync(_active.Id, It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal("Token 'CI' revoked.", target.SuccessMessage);
            VerifyAudited("token_revoked", "token=CI prefix=pgt_aaaa");
        }

        [Fact]
        public async Task DoesNotRevokeATokenOfAnotherUser()
        {
            var othersToken = Guid.NewGuid();
            var target = Build(_adminId);

            await target.OnPostRevokeAsync(_bob.Id, othersToken, CancellationToken.None);

            _tokens.Verify(t => t.RevokeTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Equal("Token not found.", target.ErrorMessage);
            _auditEvents.Verify(a => a.AddAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ReportsAnAlreadyRevokedToken()
        {
            var target = Build(_adminId);

            await target.OnPostRevokeAsync(_bob.Id, _revoked.Id, CancellationToken.None);

            _tokens.Verify(t => t.RevokeTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Equal("Token not found.", target.ErrorMessage);
        }

        [Fact]
        public async Task RedirectsNonAdmins()
        {
            var result = await Build(_bob.Id).OnPostRevokeAsync(_bob.Id, _active.Id, CancellationToken.None);

            Assert.IsType<RedirectToPageResult>(result);
            _tokens.Verify(t => t.RevokeTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    public class OnPostRevokeAllAsync : FactsBase
    {
        [Fact]
        public async Task RevokesOnlyActiveTokens()
        {
            var second = Token("Laptop", "pgt_dddd", expiresInDays: 10);
            _bobTokens.Add(second);
            var target = Build(_adminId);

            await target.OnPostRevokeAllAsync(_bob.Id, CancellationToken.None);

            _tokens.Verify(t => t.RevokeTokenAsync(_active.Id, It.IsAny<CancellationToken>()), Times.Once);
            _tokens.Verify(t => t.RevokeTokenAsync(second.Id, It.IsAny<CancellationToken>()), Times.Once);
            _tokens.Verify(t => t.RevokeTokenAsync(_revoked.Id, It.IsAny<CancellationToken>()), Times.Never);
            _tokens.Verify(t => t.RevokeTokenAsync(_expired.Id, It.IsAny<CancellationToken>()), Times.Never);
            Assert.Equal("2 tokens revoked.", target.SuccessMessage);
            VerifyAudited("tokens_revoked_all", "count=2");
        }

        [Fact]
        public async Task ReportsWhenNothingIsActive()
        {
            _bobTokens.Remove(_active);
            var target = Build(_adminId);

            await target.OnPostRevokeAllAsync(_bob.Id, CancellationToken.None);

            _tokens.Verify(t => t.RevokeTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Equal("'bob' has no active tokens.", target.ErrorMessage);
        }

        [Fact]
        public async Task RedirectsNonAdmins()
        {
            var result = await Build(_bob.Id).OnPostRevokeAllAsync(_bob.Id, CancellationToken.None);

            Assert.IsType<RedirectToPageResult>(result);
            _tokens.Verify(t => t.RevokeTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    public class FactsBase
    {
        protected static readonly DateTime Now = new(2020, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        protected readonly Guid _adminId = Guid.NewGuid();
        protected readonly User _bob = new() { Id = Guid.NewGuid(), Username = "bob" };
        protected readonly PersonalAccessToken _active;
        protected readonly PersonalAccessToken _revoked;
        protected readonly PersonalAccessToken _expired;
        protected readonly List<PersonalAccessToken> _bobTokens;

        protected readonly Mock<IUserService> _users = new();
        protected readonly Mock<ITokenService> _tokens = new();
        protected readonly Mock<IAuditEventService> _auditEvents = new();

        protected FactsBase()
        {
            _active = Token("CI", "pgt_aaaa", expiresInDays: 30);
            _revoked = Token("Old", "pgt_bbbb", expiresInDays: 30);
            _revoked.IsRevoked = true;
            _expired = Token("Expired", "pgt_cccc", expiresInDays: -1);
            _bobTokens = [_active, _revoked, _expired];

            _users.Setup(u => u.IsAdminAsync(_adminId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _users.Setup(u => u.FindByIdAsync(_bob.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_bob);
            _tokens
                .Setup(t => t.GetUserTokensAsync(_bob.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => [.. _bobTokens]);
        }

        protected PersonalAccessToken Token(string name, string prefix, int expiresInDays)
        {
            return new PersonalAccessToken
            {
                Id = Guid.NewGuid(),
                UserId = _bob.Id,
                Name = name,
                TokenPrefix = prefix,
                CreatedAtUtc = Now.AddDays(-60),
                ExpiresAtUtc = Now.AddDays(expiresInDays),
            };
        }

        protected UserTokensModel Build(Guid signedInUserId)
        {
            var time = new Mock<SystemTime>();
            time.Setup(t => t.UtcNow).Returns(Now);

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, signedInUserId.ToString()), new Claim(ClaimTypes.Name, "admin") }, "TestAuth"));

            return new UserTokensModel(_users.Object, _tokens.Object, time.Object, TestWebAuditLog.Create(auditEvents: _auditEvents.Object))
            {
                PageContext = new PageContext(new ActionContext(
                    new DefaultHttpContext { User = principal },
                    new RouteData(),
                    new PageActionDescriptor())),
            };
        }

        protected void VerifyAudited(string eventName, string detail)
        {
            _auditEvents.Verify(a => a.AddAsync(
                It.Is<AuditEvent>(e => e.Event == eventName && e.Target == "bob" && e.Detail == detail && e.Actor == "admin"),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
