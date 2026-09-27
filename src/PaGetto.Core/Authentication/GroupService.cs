using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PaGetto.Core.Authentication;

public partial class GroupService : IGroupService
{
    private readonly IContext _context;
    private readonly ILogger<GroupService> _logger;

    public GroupService(IContext context, ILogger<GroupService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Group> FindByIdAsync(Guid groupId, CancellationToken cancellationToken)
    {
        return await _context.Groups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
    }

    public async Task<Group> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        if (name == null) return null;

        // Case-insensitive on every database, like usernames (see UserService.FindByUsernameAsync).
        var normalized = Group.NormalizeName(name);
        return await _context.Groups.FirstOrDefaultAsync(g => g.NormalizedName == normalized, cancellationToken);
    }

    public async Task<Group> FindByAppRoleValueAsync(string appRoleValue, CancellationToken cancellationToken)
    {
        return await _context.Groups.FirstOrDefaultAsync(
            g => g.AppRoleValue == appRoleValue, cancellationToken);
    }

    public async Task<Group> CreateGroupAsync(
        string name,
        string appRoleValue,
        string description,
        CancellationToken cancellationToken)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            AppRoleValue = appRoleValue,
            Description = description,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Groups.Add(group);
        await _context.SaveChangesAsync(cancellationToken);

        LogGroupCreated(name, group.Id);
        return group;
    }

    public async Task<bool> UpdateGroupAsync(
        Guid groupId,
        string name,
        string description,
        CancellationToken cancellationToken)
    {
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (group == null) return false;

        var oldName = group.Name;
        group.Name = name;
        group.Description = description;
        await _context.SaveChangesAsync(cancellationToken);

        LogGroupUpdated(oldName, name, groupId);
        return true;
    }

    public async Task<List<Group>> GetAllGroupsAsync(CancellationToken cancellationToken)
    {
        return await _context.Groups
            .Include(g => g.UserGroups)
            .ThenInclude(ug => ug.User)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Group>> GetUserGroupsAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.UserGroups
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.Group)
            .ToListAsync(cancellationToken);
    }

    public async Task AddUserToGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken)
    {
        if (!await CanManuallyModifyMembershipAsync(groupId, userId, cancellationToken))
        {
            throw new InvalidOperationException(
                "Cannot manually add an Entra user to a role-linked group. Membership is managed by App Roles.");
        }

        var exists = await _context.UserGroups
            .AnyAsync(ug => ug.UserId == userId && ug.GroupId == groupId, cancellationToken);

        if (exists) return;

        _context.UserGroups.Add(new UserGroup
        {
            UserId = userId,
            GroupId = groupId
        });

        await _context.SaveChangesAsync(cancellationToken);
        LogUserAddedToGroup(userId, groupId);
    }

    public async Task RemoveUserFromGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken)
    {
        if (!await CanManuallyModifyMembershipAsync(groupId, userId, cancellationToken))
        {
            throw new InvalidOperationException(
                "Cannot manually remove an Entra user from a role-linked group. Membership is managed by App Roles.");
        }

        var membership = await _context.UserGroups
            .FirstOrDefaultAsync(ug => ug.UserId == userId && ug.GroupId == groupId, cancellationToken);

        if (membership == null) return;

        _context.UserGroups.Remove(membership);
        await _context.SaveChangesAsync(cancellationToken);
        LogUserRemovedFromGroup(userId, groupId);
    }

    public async Task SyncAppRoleMembershipsAsync(
        Guid userId,
        IReadOnlyList<string> appRoleValues,
        CancellationToken cancellationToken)
    {
        // Get all role-linked groups the user is currently a member of
        var currentMemberships = await _context.UserGroups
            .Where(ug => ug.UserId == userId && ug.Group.AppRoleValue != null)
            .Include(ug => ug.Group)
            .ToListAsync(cancellationToken);

        var currentAppRoleValues = currentMemberships
            .Select(ug => ug.Group.AppRoleValue)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Add memberships for new App Roles
        var appRoleValueSet = appRoleValues.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newRoles = appRoleValueSet.Where(r => !currentAppRoleValues.Contains(r)).ToList();

        // Fetch all groups
        var groupsForNewRoles = newRoles.Count > 0
            ? await _context.Groups
                .Where(g => g.AppRoleValue != null && newRoles.Contains(g.AppRoleValue))
                .ToListAsync(cancellationToken)
            : [];

        foreach (var group in groupsForNewRoles)
        {
            _context.UserGroups.Add(new UserGroup
            {
                UserId = userId,
                GroupId = group.Id
            });
        }

        // Remove memberships for role-linked groups whose role is no longer in the token
        foreach (var membership in currentMemberships)
        {
            if (!appRoleValueSet.Contains(membership.Group.AppRoleValue))
            {
                _context.UserGroups.Remove(membership);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        LogAppRoleMembershipsSynced(userId, appRoleValues.Count);
    }

    public async Task<bool> IsRoleLinkedGroupAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        return group?.AppRoleValue != null;
    }

    public async Task<bool> CanManuallyModifyMembershipAsync(Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (group?.AppRoleValue == null)
            return true; // Manually-managed group: always allow

        // Role-linked group: only allow for local users
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
            return false;

        return user.AuthProvider != AuthProvider.Entra;
    }

    public async Task<bool> DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await _context.Groups
            .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (group == null) return false;

        var memberships = await _context.UserGroups
            .Where(ug => ug.GroupId == groupId)
            .ToListAsync(cancellationToken);
        _context.UserGroups.RemoveRange(memberships);

        var permissions = await _context.FeedPermissions
            .Where(fp => fp.PrincipalId == groupId && fp.PrincipalType == PrincipalType.Group)
            .ToListAsync(cancellationToken);
        _context.FeedPermissions.RemoveRange(permissions);

        _context.Groups.Remove(group);
        await _context.SaveChangesAsync(cancellationToken);

        LogGroupDeleted(group.Name, groupId);

        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created group {GroupName} with ID {GroupId}")]
    private partial void LogGroupCreated(string groupName, Guid groupId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated group {OldGroupName} to {GroupName} (ID: {GroupId})")]
    private partial void LogGroupUpdated(string oldGroupName, string groupName, Guid groupId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Added user {UserId} to group {GroupId}")]
    private partial void LogUserAddedToGroup(Guid userId, Guid groupId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed user {UserId} from group {GroupId}")]
    private partial void LogUserRemovedFromGroup(Guid userId, Guid groupId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Synced App Role memberships for user {UserId}: {RoleCount} roles")]
    private partial void LogAppRoleMembershipsSynced(Guid userId, int roleCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted group {GroupName} (ID: {GroupId})")]
    private partial void LogGroupDeleted(string groupName, Guid groupId);
}
