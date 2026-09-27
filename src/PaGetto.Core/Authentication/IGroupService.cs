using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Authentication;

public interface IGroupService
{
    Task<Group> FindByIdAsync(Guid groupId, CancellationToken cancellationToken);
    Task<Group> FindByNameAsync(string name, CancellationToken cancellationToken);
    Task<Group> FindByAppRoleValueAsync(string appRoleValue, CancellationToken cancellationToken);
    Task<Group> CreateGroupAsync(string name, string appRoleValue, string description, CancellationToken cancellationToken);
    /// <summary>
    /// Changes the name and description. Members, feed permissions and the app role value are kept.
    /// The caller checks that the name isn't taken by another group.
    /// </summary>
    /// <returns>False when the group doesn't exist.</returns>
    Task<bool> UpdateGroupAsync(Guid groupId, string name, string description, CancellationToken cancellationToken);
    Task<List<Group>> GetAllGroupsAsync(CancellationToken cancellationToken);
    Task<List<Group>> GetUserGroupsAsync(Guid userId, CancellationToken cancellationToken);
    Task AddUserToGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken);
    Task RemoveUserFromGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken);
    Task SyncAppRoleMembershipsAsync(Guid userId, IReadOnlyList<string> appRoleValues, CancellationToken cancellationToken);
    Task<bool> IsRoleLinkedGroupAsync(Guid groupId, CancellationToken cancellationToken);
    Task<bool> CanManuallyModifyMembershipAsync(Guid groupId, Guid userId, CancellationToken cancellationToken);
    /// <returns>False when the group doesn't exist.</returns>
    Task<bool> DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken);
}
