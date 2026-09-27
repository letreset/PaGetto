using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace PaGetto.Web.Pages.Account;

[Authorize(AuthenticationSchemes = Core.Authentication.AuthenticationConstants.CookieScheme)]
public class ChangePasswordModel : PageModel
{
    private readonly IUserService _userService;
    private readonly IOptionsSnapshot<NugetAuthenticationOptions> _authOptions;

    public ChangePasswordModel(IUserService userService, IOptionsSnapshot<NugetAuthenticationOptions> authOptions)
    {
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _authOptions = authOptions ?? throw new ArgumentNullException(nameof(authOptions));
    }

    public int MinPasswordLength => _authOptions.Value.MinPasswordLength;

    [BindProperty]
    [Required(ErrorMessage = "Current password is required.")]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "New password is required.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Confirm the new password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords don't match.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ReturnUrl { get; set; }

    public bool IsRequired { get; private set; }

    public bool IsLocalAccount { get; private set; }

    [TempData]
    public string SuccessMessage { get; set; }

    public string ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var user = await FindCurrentUserAsync(cancellationToken);
        if (user == null) return RedirectToPage("/Login");

        Load(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = await FindCurrentUserAsync(cancellationToken);
        if (user == null) return RedirectToPage("/Login");

        Load(user);
        if (!IsLocalAccount) return Page();

        if (NewPassword?.Length < MinPasswordLength)
            ModelState.AddModelError(nameof(NewPassword), $"Password must be at least {MinPasswordLength} characters.");

        if (!ModelState.IsValid) return Page();

        if (!await _userService.VerifyPasswordAsync(user, CurrentPassword))
        {
            ErrorMessage = "The current password is incorrect.";
            return Page();
        }

        if (string.Equals(CurrentPassword, NewPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "The new password must be different from the current one.";
            return Page();
        }

        await _userService.ChangeOwnPasswordAsync(user.Id, NewPassword, cancellationToken);

        if (IsRequired)
        {
            return !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
                ? LocalRedirect(ReturnUrl)
                : RedirectToPage("/Index");
        }

        SuccessMessage = "Your password has been changed.";
        return RedirectToPage();
    }

    private void Load(User user)
    {
        IsLocalAccount = user.AuthProvider == AuthProvider.Local;
        IsRequired = user.MustChangePassword;
    }

    private async Task<User> FindCurrentUserAsync(CancellationToken cancellationToken)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null || !Guid.TryParse(claim.Value, out var userId)) return null;

        return await _userService.FindByIdAsync(userId, cancellationToken);
    }
}
