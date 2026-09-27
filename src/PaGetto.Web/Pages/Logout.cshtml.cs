using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace PaGetto.Web.Pages;

public partial class LogoutModel : PageModel
{
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(
        ILogger<LogoutModel> logger)
    {
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var username = User.Identity?.Name;

        await HttpContext.SignOutAsync(Core.Authentication.AuthenticationConstants.CookieScheme);

        LogSignedOut(username);

        return RedirectToPage("/Index");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User '{Username}' signed out")]
    private partial void LogSignedOut(string username);
}
