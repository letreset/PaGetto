using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Audit;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Web.Pages.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Moq;
using Xunit;

namespace PaGetto.Web.Tests.Pages.Admin;

public class AuditModelFacts
{
    public class OnGetAsync : FactsBase
    {
        [Fact]
        public async Task RedirectsNonAdmins()
        {
            var target = Build(Guid.NewGuid());

            var result = await target.OnGetAsync(CancellationToken.None);

            Assert.Equal("/Index", Assert.IsType<RedirectToPageResult>(result).PageName);
            _auditEvents.Verify(a => a.SearchAsync(
                It.IsAny<AuditEventFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PassesTheFiltersWithAnInclusiveDateRange()
        {
            AuditEventFilter filter = null;
            _auditEvents
                .Setup(a => a.SearchAsync(It.IsAny<AuditEventFilter>(), 0, AuditModel.PageSize, It.IsAny<CancellationToken>()))
                .Callback((AuditEventFilter f, int _, int _, CancellationToken _) => filter = f)
                .ReturnsAsync((new List<AuditEvent>(), 0));
            var target = Build(_adminId);
            target.Event = "package_delete_succeeded";
            target.Actor = " alice ";
            target.Feed = "internal";
            target.Target = " Contoso ";
            target.From = new DateTime(2020, 1, 2);
            target.To = new DateTime(2020, 1, 5);

            Assert.IsType<PageResult>(await target.OnGetAsync(CancellationToken.None));

            Assert.Equal("package_delete_succeeded", filter.Event);
            Assert.Equal("alice", filter.Actor);
            Assert.Equal("internal", filter.Feed);
            Assert.Equal("Contoso", filter.Target);
            Assert.Equal(new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc), filter.FromUtc);
            Assert.Equal(DateTimeKind.Utc, filter.FromUtc.Value.Kind);
            Assert.Equal(new DateTime(2020, 1, 6, 0, 0, 0, DateTimeKind.Utc), filter.BeforeUtc);
        }

        [Fact]
        public async Task PagesTheResults()
        {
            var page = new List<AuditEvent> { new() { Event = "group_created" } };
            _auditEvents
                .Setup(a => a.SearchAsync(It.IsAny<AuditEventFilter>(), 2 * AuditModel.PageSize, AuditModel.PageSize, It.IsAny<CancellationToken>()))
                .ReturnsAsync((page, 2 * AuditModel.PageSize + 1));
            var target = Build(_adminId);
            target.PageIndex = 3;

            await target.OnGetAsync(CancellationToken.None);

            Assert.Same(page, target.Events);
            Assert.Equal(2 * AuditModel.PageSize + 1, target.TotalCount);
            Assert.Equal(3, target.TotalPages);
        }

        [Fact]
        public async Task TreatsAPageBelowOneAsTheFirstPage()
        {
            _auditEvents
                .Setup(a => a.SearchAsync(It.IsAny<AuditEventFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((new List<AuditEvent>(), 0));
            var target = Build(_adminId);
            target.PageIndex = -4;

            await target.OnGetAsync(CancellationToken.None);

            Assert.Equal(1, target.PageIndex);
            Assert.Equal(1, target.TotalPages);
            _auditEvents.Verify(a => a.SearchAsync(
                It.IsAny<AuditEventFilter>(), 0, AuditModel.PageSize, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    public class FactsBase
    {
        protected readonly Guid _adminId = Guid.NewGuid();
        protected readonly Mock<IAuditEventService> _auditEvents = new();
        protected readonly Mock<IUserService> _users = new();
        protected readonly Mock<IFeedService> _feeds = new();

        protected FactsBase()
        {
            _users.Setup(u => u.IsAdminAsync(_adminId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _feeds.Setup(f => f.GetAllFeedsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Feed>());
            _auditEvents.Setup(a => a.GetEventNamesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        }

        protected AuditModel Build(Guid userId)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "TestAuth"));

            return new AuditModel(_auditEvents.Object, _users.Object, _feeds.Object)
            {
                PageContext = new PageContext(new ActionContext(
                    new DefaultHttpContext { User = principal },
                    new RouteData(),
                    new PageActionDescriptor())),
            };
        }
    }
}
