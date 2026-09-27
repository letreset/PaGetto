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
/// Creates the default local administrator (<see cref="DefaultUsername"/> / <see cref="DefaultPassword"/>)
/// on startup, so a fresh Local or Hybrid install has a way into Admin &gt; Accounts. The account has to
/// change its password on the first web sign-in and can't be used by NuGet clients before that.
/// It only acts while no administrator can manage the server (no enabled administrator with web
/// sign-in), and never changes an existing user.
/// </summary>
public partial class InitialAdminSeeder
{
    public const string DefaultUsername = "admin";
    public const string DefaultPassword = "admin";

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
        if (_authOptions.Mode is not (AuthenticationMode.Local or AuthenticationMode.Hybrid))
            return;

        if (await _context.Users.AnyAsync(u => u.IsAdmin && u.IsEnabled && u.CanLoginToUI, cancellationToken))
            return;

        if (await _userService.FindByUsernameAsync(DefaultUsername, cancellationToken) != null)
        {
            LogDefaultAdminNameTaken(DefaultUsername);
            return;
        }

        try
        {
            var user = await _userService.CreateLocalAdminAsync(
                DefaultUsername, DefaultPassword, mustChangePassword: true, cancellationToken);

            LogInitialAdminCreated("InitialAdminCreated", DefaultUsername, user.Id);
        }
        catch (DbUpdateException ex) when (_context.IsUniqueConstraintViolationException(ex))
        {
            // Another replica starting at the same time won the insert.
            LogInitialAdminCreatedByAnotherInstance(DefaultUsername);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No enabled administrator with web sign-in exists, but a user named {Username} already exists, so the default administrator was not created. The user was left unchanged.")]
    private partial void LogDefaultAdminNameTaken(string username);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit: {EventType} - Created the default administrator {Username} with ID {UserId} and the default password. Sign in to the web UI and change the password.")]
    private partial void LogInitialAdminCreated(string eventType, string username, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The initial administrator {Username} was created by another instance; skipping.")]
    private partial void LogInitialAdminCreatedByAnotherInstance(string username);
}
