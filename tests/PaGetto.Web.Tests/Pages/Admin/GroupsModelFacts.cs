using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Web.Audit;
using PaGetto.Web.Pages.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace PaGetto.Web.Tests.Pages.Admin;

public class GroupsModelFacts
{
    public class OnPostSavePermissionsAsync : FactsBase
    {
        [Fact]
        public async Task GrantsRevokesAndSkipsEachRowInOnePost()
        {
            var groupId = Guid.NewGuid();
            var grantFeed = Guid.NewGuid();
            var revokeFeed = Guid.NewGuid();
            var emptyRow = Guid.Empty;

            var existing = new FeedPermission { Id = Guid.NewGuid() };
            _permissions
                .Setup(p => p.GetPermissionAsync(groupId, PrincipalType.Group, revokeFeed, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existing);

            var input = new List<FeedPermissionInput>
            {
                new() { FeedId = grantFeed, CanPull = true, CanPush = true, CanDelete = true },
                new() { FeedId = revokeFeed, CanPull = false, CanPush = false, CanDelete = false },
                new() { FeedId = emptyRow, CanPull = true, CanPush = true },
            };

            var result = await _target.OnPostSavePermissionsAsync(groupId, input, CancellationToken.None);

            // Enabled row is granted, including the delete permission.
            _permissions.Verify(p => p.GrantPermissionAsync(
                groupId, PrincipalType.Group, grantFeed, true, true,
                It.IsAny<CancellationToken>(), It.IsAny<PermissionSource>(), true), Times.Once);

            // Unchecking pull, push and delete revokes the existing permission.
            _permissions.Verify(p => p.RevokePermissionAsync(existing.Id, It.IsAny<CancellationToken>()), Times.Once);

            // The empty-feed row is skipped entirely.
            _permissions.Verify(p => p.GrantPermissionAsync(
                groupId, PrincipalType.Group, emptyRow, It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>(), It.IsAny<PermissionSource>(), It.IsAny<bool>()), Times.Never);

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(groupId, redirect.RouteValues["savedGroupId"]);
        }

        [Fact]
        public async Task LeavesUnchangedRowsAlone()
        {
            var groupId = Guid.NewGuid();
            var feedId = Guid.NewGuid();
            _permissions
                .Setup(p => p.GetPermissionAsync(groupId, PrincipalType.Group, feedId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FeedPermission { Id = Guid.NewGuid(), CanPull = true, CanPush = false, CanDelete = false });

            await _target.OnPostSavePermissionsAsync(
                groupId,
                [new() { FeedId = feedId, CanPull = true, CanPush = false, CanDelete = false }],
                CancellationToken.None);

            _permissions.Verify(p => p.GrantPermissionAsync(
                It.IsAny<Guid>(), It.IsAny<PrincipalType>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>(), It.IsAny<PermissionSource>(), It.IsAny<bool>()), Times.Never);
            _permissions.Verify(p => p.RevokePermissionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    public class OnPostUpdateGroupAsync : FactsBase
    {
        private readonly Group _group = new() { Id = Guid.NewGuid(), Name = "Developers", Description = "Old" };

        public OnPostUpdateGroupAsync()
        {
            _groups.Setup(g => g.GetAllGroupsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Group>());
            _groups.Setup(g => g.FindByIdAsync(_group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_group);
            _users.Setup(u => u.GetAllUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<User>());
            _permissions
                .Setup(p => p.GetPermissionsByPrincipalTypeAsync(PrincipalType.Group, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<FeedPermission>());
        }

        [Fact]
        public async Task UpdatesTheGroup()
        {
            _groups.Setup(g => g.UpdateGroupAsync(_group.Id, "Engineers", "New", It.IsAny<CancellationToken>())).ReturnsAsync(true);

            await _target.OnPostUpdateGroupAsync(_group.Id, "Engineers", "New", CancellationToken.None);

            _groups.Verify(g => g.UpdateGroupAsync(_group.Id, "Engineers", "New", It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal("Group 'Engineers' updated successfully.", _target.SuccessMessage);
            Assert.Null(_target.ErrorMessage);
            Assert.Null(_target.EditGroupId);
        }

        [Fact]
        public async Task AllowsChangingTheCaseOfItsOwnName()
        {
            _groups.Setup(g => g.FindByNameAsync("developers", It.IsAny<CancellationToken>())).ReturnsAsync(_group);
            _groups.Setup(g => g.UpdateGroupAsync(_group.Id, "developers", "Old", It.IsAny<CancellationToken>())).ReturnsAsync(true);

            await _target.OnPostUpdateGroupAsync(_group.Id, "developers", "Old", CancellationToken.None);

            _groups.Verify(g => g.UpdateGroupAsync(_group.Id, "developers", "Old", It.IsAny<CancellationToken>()), Times.Once);
            Assert.Null(_target.ErrorMessage);
        }

        [Fact]
        public async Task RejectsTheNameOfAnotherGroup()
        {
            _groups
                .Setup(g => g.FindByNameAsync("Testers", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Group { Id = Guid.NewGuid(), Name = "Testers" });

            await _target.OnPostUpdateGroupAsync(_group.Id, "Testers", "New", CancellationToken.None);

            _groups.Verify(g => g.UpdateGroupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Equal("Group 'Testers' already exists.", _target.ErrorMessage);
            Assert.Equal(_group.Id, _target.EditGroupId);
            Assert.Equal("Testers", _target.EditName);
            Assert.Equal("New", _target.EditDescription);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("   ")]
        public async Task RequiresAName(string name)
        {
            await _target.OnPostUpdateGroupAsync(_group.Id, name, null, CancellationToken.None);

            _groups.Verify(g => g.UpdateGroupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Equal("Group name is required.", _target.ErrorMessage);
        }

        [Fact]
        public async Task ReportsAnUnknownGroup()
        {
            await _target.OnPostUpdateGroupAsync(Guid.NewGuid(), "Engineers", null, CancellationToken.None);

            _groups.Verify(g => g.UpdateGroupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Equal("Group not found.", _target.ErrorMessage);
            Assert.Null(_target.EditGroupId);
        }
    }

    public class OnPostDeleteGroupAsync : FactsBase
    {
        public OnPostDeleteGroupAsync()
        {
            _groups.Setup(g => g.GetAllGroupsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Group>());
            _users.Setup(u => u.GetAllUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<User>());
            _permissions
                .Setup(p => p.GetPermissionsByPrincipalTypeAsync(PrincipalType.Group, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<FeedPermission>());
        }

        [Fact]
        public async Task ReportsSuccess()
        {
            var groupId = Guid.NewGuid();
            _groups.Setup(g => g.DeleteGroupAsync(groupId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

            await _target.OnPostDeleteGroupAsync(groupId, CancellationToken.None);

            Assert.Equal("Group deleted successfully.", _target.SuccessMessage);
            Assert.Null(_target.ErrorMessage);
        }

        [Fact]
        public async Task ReportsAnUnknownGroup()
        {
            _groups.Setup(g => g.DeleteGroupAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            await _target.OnPostDeleteGroupAsync(Guid.NewGuid(), CancellationToken.None);

            Assert.Equal("Group not found.", _target.ErrorMessage);
            Assert.Null(_target.SuccessMessage);
        }
    }

    public class FactsBase
    {
        protected readonly Mock<IGroupService> _groups = new();
        protected readonly Mock<IUserService> _users = new();
        protected readonly Mock<IPermissionService> _permissions = new();
        protected readonly Mock<IFeedService> _feeds = new();
        protected readonly GroupsModel _target;

        protected FactsBase()
        {
            var adminId = Guid.NewGuid();
            _users.Setup(u => u.IsAdminAsync(adminId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _feeds.Setup(f => f.GetAllFeedsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Feed>());

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, adminId.ToString()) }, "TestAuth"));

            _target = new GroupsModel(_groups.Object, _users.Object, _permissions.Object, _feeds.Object, new WebAuditLog(NullLogger<WebAuditLog>.Instance))
            {
                PageContext = new PageContext(new ActionContext(
                    new DefaultHttpContext { User = principal },
                    new RouteData(),
                    new PageActionDescriptor())),
            };
        }
    }
}
