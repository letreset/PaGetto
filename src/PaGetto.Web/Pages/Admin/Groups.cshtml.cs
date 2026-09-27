using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Web.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PaGetto.Web.Pages.Admin;

[Authorize(AuthenticationSchemes = Core.Authentication.AuthenticationConstants.CookieScheme)]
public class GroupsModel : PageModel
{
    private readonly IGroupService _groupService;
    private readonly IUserService _userService;
    private readonly IPermissionService _permissionService;
    private readonly IFeedService _feedService;
    private readonly WebAuditLog _audit;

    public GroupsModel(
        IGroupService groupService,
        IUserService userService,
        IPermissionService permissionService,
        IFeedService feedService,
        WebAuditLog audit)
    {
        _groupService = groupService ?? throw new ArgumentNullException(nameof(groupService));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _feedService = feedService ?? throw new ArgumentNullException(nameof(feedService));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public List<Group> Groups { get; set; } = new();
    public List<User> AllUsers { get; set; } = new();
    public List<Feed> AllFeeds { get; set; } = new();
    public Dictionary<Guid, Dictionary<Guid, FeedPermission>> GroupPermissions { get; set; } = new();

    [FromQuery]
    public Guid? SavedGroupId { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Group name is required.")]
    [MaxLength(256)]
    public string NewGroupName { get; set; }

    [BindProperty]
    [MaxLength(128)]
    public string NewAppRoleValue { get; set; }

    [BindProperty]
    [MaxLength(4000)]
    public string NewDescription { get; set; }

    public string SuccessMessage { get; set; }
    public string ErrorMessage { get; set; }

    /// <summary>
    /// The create form's properties are bound (and validated) for every POST. Only the CreateGroup handler
    /// uses them, so other handlers drop their errors instead of showing "Group name is required." under the form.
    /// </summary>
    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if (context.HandlerMethod?.MethodInfo.Name != nameof(OnPostCreateGroupAsync))
        {
            ModelState.Clear();
        }
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
    }

    private async Task<bool> IsCurrentUserAdminAsync(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return false;
        return await _userService.IsAdminAsync(userId, cancellationToken);
    }

    private async Task LoadGroupPermissionsAsync(CancellationToken cancellationToken)
    {
        AllFeeds = await _feedService.GetAllFeedsAsync(cancellationToken);

        var groupPermissions = await _permissionService.GetPermissionsByPrincipalTypeAsync(
            PrincipalType.Group, cancellationToken);

        GroupPermissions = new Dictionary<Guid, Dictionary<Guid, FeedPermission>>(Groups.Count);
        foreach (var group in Groups)
        {
            GroupPermissions[group.Id] = new Dictionary<Guid, FeedPermission>();
        }

        foreach (var permission in groupPermissions)
        {
            if (GroupPermissions.TryGetValue(permission.PrincipalId, out var perFeed))
            {
                perFeed[permission.FeedId] = permission;
            }
        }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        Groups = await _groupService.GetAllGroupsAsync(cancellationToken);
        AllUsers = await _userService.GetAllUsersAsync(cancellationToken);
        await LoadGroupPermissionsAsync(cancellationToken);
        return Page();
    }

    private async Task<string> GetGroupNameAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await _groupService.FindByIdAsync(groupId, cancellationToken);
        return group?.Name ?? groupId.ToString();
    }

    private async Task AuditMembershipAsync(string eventName, Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        _audit.Admin(HttpContext, eventName, await GetGroupNameAsync(groupId, cancellationToken), $"user={user?.Username ?? userId.ToString()}");
    }

    public async Task<IActionResult> OnPostCreateGroupAsync(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        if (!ModelState.IsValid)
        {
            Groups = await _groupService.GetAllGroupsAsync(cancellationToken);
            AllUsers = await _userService.GetAllUsersAsync(cancellationToken);
            await LoadGroupPermissionsAsync(cancellationToken);
            return Page();
        }

        var existing = await _groupService.FindByNameAsync(NewGroupName, cancellationToken);
        if (existing != null)
        {
            ErrorMessage = $"Group '{NewGroupName}' already exists.";
            Groups = await _groupService.GetAllGroupsAsync(cancellationToken);
            AllUsers = await _userService.GetAllUsersAsync(cancellationToken);
            await LoadGroupPermissionsAsync(cancellationToken);
            return Page();
        }

        await _groupService.CreateGroupAsync(
            NewGroupName,
            string.IsNullOrWhiteSpace(NewAppRoleValue) ? null : NewAppRoleValue.Trim(),
            NewDescription,
            cancellationToken);

        _audit.Admin(HttpContext, "group_created", NewGroupName);
        SuccessMessage = $"Group '{NewGroupName}' created successfully.";
        Groups = await _groupService.GetAllGroupsAsync(cancellationToken);
        AllUsers = await _userService.GetAllUsersAsync(cancellationToken);
        await LoadGroupPermissionsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAddUserAsync(
        Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        if (!await _groupService.CanManuallyModifyMembershipAsync(groupId, userId, cancellationToken))
        {
            ErrorMessage = "Cannot manually add Entra users to role-linked groups. Membership is managed by Azure AD App Roles.";
            Groups = await _groupService.GetAllGroupsAsync(cancellationToken);
            AllUsers = await _userService.GetAllUsersAsync(cancellationToken);
            await LoadGroupPermissionsAsync(cancellationToken);
            return Page();
        }

        await _groupService.AddUserToGroupAsync(userId, groupId, cancellationToken);
        await AuditMembershipAsync("group_member_added", groupId, userId, cancellationToken);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveUserAsync(
        Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        if (!await _groupService.CanManuallyModifyMembershipAsync(groupId, userId, cancellationToken))
        {
            ErrorMessage = "Cannot manually remove Entra users from role-linked groups. Membership is managed by Azure AD App Roles.";
            Groups = await _groupService.GetAllGroupsAsync(cancellationToken);
            AllUsers = await _userService.GetAllUsersAsync(cancellationToken);
            await LoadGroupPermissionsAsync(cancellationToken);
            return Page();
        }

        await _groupService.RemoveUserFromGroupAsync(userId, groupId, cancellationToken);
        await AuditMembershipAsync("group_member_removed", groupId, userId, cancellationToken);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSavePermissionsAsync(
        Guid groupId,
        List<FeedPermissionInput> permissions,
        CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        permissions ??= new List<FeedPermissionInput>();
        var groupName = await GetGroupNameAsync(groupId, cancellationToken);
        var feedSlugs = (await _feedService.GetAllFeedsAsync(cancellationToken)).ToDictionary(f => f.Id, f => f.Slug);
        foreach (var permission in permissions)
        {
            if (permission.FeedId == Guid.Empty)
                continue;

            var existing = await _permissionService.GetPermissionAsync(
                groupId, PrincipalType.Group, permission.FeedId, cancellationToken);

            // Unchecking every permission revokes the row so we don't persist an
            // all-false row that has no effect.
            if (!permission.CanPush && !permission.CanPull && !permission.CanDelete)
            {
                if (existing != null)
                {
                    await _permissionService.RevokePermissionAsync(existing.Id, cancellationToken);
                    _audit.Admin(HttpContext, "feed_permission_revoked", groupName,
                        $"feed={feedSlugs.GetValueOrDefault(permission.FeedId)}");
                }
            }
            // Save all posts every feed row; only changed rows are written and audited.
            else if (existing == null
                || existing.CanPull != permission.CanPull
                || existing.CanPush != permission.CanPush
                || existing.CanDelete != permission.CanDelete)
            {
                await _permissionService.GrantPermissionAsync(
                    groupId, PrincipalType.Group, permission.FeedId,
                    permission.CanPush, permission.CanPull, cancellationToken,
                    canDelete: permission.CanDelete);
                _audit.Admin(HttpContext, "feed_permission_set", groupName,
                    $"feed={feedSlugs.GetValueOrDefault(permission.FeedId)} pull={permission.CanPull} push={permission.CanPush} delete={permission.CanDelete}");
            }
        }

        return RedirectToPage(new { savedGroupId = groupId });
    }

    public async Task<IActionResult> OnPostRevokePermissionAsync(
        Guid permissionId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        await _permissionService.RevokePermissionAsync(permissionId, cancellationToken);
        _audit.Admin(HttpContext, "feed_permission_revoked", permissionId.ToString());

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteGroupAsync(
        Guid groupId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        var deletedName = await GetGroupNameAsync(groupId, cancellationToken);
        if (await _groupService.DeleteGroupAsync(groupId, cancellationToken))
        {
            _audit.Admin(HttpContext, "group_deleted", deletedName);
            SuccessMessage = "Group deleted successfully.";
        }
        else
        {
            ErrorMessage = "Group not found.";
        }

        Groups = await _groupService.GetAllGroupsAsync(cancellationToken);
        AllUsers = await _userService.GetAllUsersAsync(cancellationToken);
        await LoadGroupPermissionsAsync(cancellationToken);
        return Page();
    }
}
