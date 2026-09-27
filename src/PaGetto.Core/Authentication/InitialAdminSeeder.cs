using System;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaGetto.Core.Authentication;

/// <summary>
/// Creates the local administrator from <c>Authentication:InitialAdmin</c> on startup, so a fresh
/// Local or Hybrid install has a way into Admin &gt; Accounts. It only acts while no administrator
/// can manage the server (no enabled administrator with web sign-in), and never changes an existing
/// user: to recover from a disabled last administrator, configure a new username.
/// </summary>
public partial class InitialAdminSeeder
{
    private readonly IContext _context;
    private readonly IUserService _userService;
    private readonly NugetAuthenticationOptions _authOptions;
    private readonly ILogger<InitialAdminSeeder> _logger;

    public InitialAdminSeeder(
        IContext context,
        IUserService userService,
        IOptionsSnapshot<NugetAuthenticationOptions> authOptions,
        ILogger<InitialAdminSeeder> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _authOptions = authOptions?.Value ?? throw new ArgumentNullException(nameof(authOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var mode = _authOptions.Mode;
        if (mode is not (AuthenticationMode.Local or AuthenticationMode.Hybrid))
            return;

        if (await _context.Users.AnyAsync(u => u.IsAdmin && u.IsEnabled && u.CanLoginToUI, cancellationToken))
            return;

        var username = _authOptions.InitialAdmin?.Username;
        var password = _authOptions.InitialAdmin?.Password;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            // Hybrid can still get an admin from the Entra Admin app role.
            if (mode == AuthenticationMode.Local)
            {
                LogInitialAdminNotConfigured();
            }

            return;
        }

        if (await _userService.FindByUsernameAsync(username, cancellationToken) != null)
        {
            LogInitialAdminExistsAsNonAdmin(username);
            return;
        }

        try
        {
            var user = await _userService.CreateLocalAdminAsync(username, password, cancellationToken);

            LogInitialAdminCreated("InitialAdminCreated", username, user.Id);
        }
        catch (DbUpdateException ex) when (_context.IsUniqueConstraintViolationException(ex))
        {
            // Another replica starting at the same time won the insert.
            LogInitialAdminCreatedByAnotherInstance(username);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No administrator exists and Authentication:InitialAdmin is not configured, so nobody can manage accounts. Set Authentication:InitialAdmin:Username and Authentication:InitialAdmin:Password to create one on startup.")]
    private partial void LogInitialAdminNotConfigured();

    [LoggerMessage(Level = LogLevel.Warning, Message = "No enabled administrator with web sign-in exists, but the configured initial administrator {Username} already exists. The user was left unchanged; choose a different Authentication:InitialAdmin:Username to create a new administrator.")]
    private partial void LogInitialAdminExistsAsNonAdmin(string username);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit: {EventType} - Created initial administrator {Username} with ID {UserId} from configuration")]
    private partial void LogInitialAdminCreated(string eventType, string username, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The initial administrator {Username} was created by another instance; skipping.")]
    private partial void LogInitialAdminCreatedByAnotherInstance(string username);
}
