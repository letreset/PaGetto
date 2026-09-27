using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaGetto.Core.Authentication;

public partial class UserService : IUserService
{
    private const int BcryptWorkFactor = 12;

    private readonly IContext _context;
    private readonly NugetAuthenticationOptions _authOptions;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IContext context,
        IOptionsSnapshot<NugetAuthenticationOptions> authOptions,
        ILogger<UserService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _authOptions = authOptions?.Value ?? throw new ArgumentNullException(nameof(authOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<User> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    public async Task<User> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        if (username == null) return null;

        // Usernames are case-insensitive whatever the column collation is (SQLite and PostgreSQL
        // compare case-sensitively): look them up by their normalized form.
        var normalized = User.NormalizeUsername(username);
        return await _context.Users.FirstOrDefaultAsync(u => u.NormalizedUsername == normalized, cancellationToken);
    }

    public async Task<User> FindByEntraObjectIdAsync(string entraObjectId, CancellationToken cancellationToken)
    {
        return await _context.Users.FirstOrDefaultAsync(
            u => u.EntraObjectId == entraObjectId, cancellationToken);
    }

    public async Task<User> CreateEntraUserAsync(
        string entraObjectId,
        string username,
        string displayName,
        string email,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = displayName,
            AuthProvider = AuthProvider.Entra,
            EntraObjectId = entraObjectId,
            Email = email,
            IsEnabled = true,
            CanLoginToUI = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        LogEntraUserCreated("AccountCreated", username, user.Id);
        return user;
    }

    public async Task<User> CreateLocalUserAsync(
        string username,
        string displayName,
        string email,
        string password,
        bool canLoginToUI,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        return await CreateLocalUserAsync(
            username, displayName, email, password, canLoginToUI, isAdmin: false, createdByUserId, cancellationToken);
    }

    public async Task<User> CreateLocalAdminAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        // A single insert, so a crash can never leave the account behind without admin rights.
        return await CreateLocalUserAsync(
            username, username, email: null, password, canLoginToUI: true, isAdmin: true, createdByUserId: null, cancellationToken);
    }

    private async Task<User> CreateLocalUserAsync(
        string username,
        string displayName,
        string email,
        string password,
        bool canLoginToUI,
        bool isAdmin,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = displayName,
            AuthProvider = AuthProvider.Local,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor),
            IsEnabled = true,
            CanLoginToUI = canLoginToUI,
            IsAdmin = isAdmin,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        LogLocalUserCreated("AccountCreated", username, user.Id, createdByUserId);
        return user;
    }

    public async Task UpdateUserAsync(User user, CancellationToken cancellationToken)
    {
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken)
    {
        var user = await FindByIdAsync(userId, cancellationToken);
        if (user == null)
            throw new InvalidOperationException($"User {userId} not found.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, BcryptWorkFactor);
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        LogPasswordUpdated("PasswordReset", userId);
    }

    public Task<bool> VerifyPasswordAsync(User user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrEmpty(user.PasswordHash)) return Task.FromResult(false);

        var result = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
        return Task.FromResult(result);
    }

    public async Task RecordFailedLoginAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await FindByIdAsync(userId, cancellationToken);
        if (user == null) return;

        user.FailedLoginCount++;
        if (user.FailedLoginCount >= _authOptions.MaxFailedAttempts)
        {
            user.LockedUntilUtc = DateTime.UtcNow.AddMinutes(_authOptions.LockoutMinutes);
            LogUserLockedOut("AccountLockedOut", userId, user.LockedUntilUtc, user.FailedLoginCount);
        }

        user.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetFailedLoginCountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await FindByIdAsync(userId, cancellationToken);
        if (user == null) return;

        user.FailedLoginCount = 0;
        user.LockedUntilUtc = null;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> IsLockedOutAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var isLocked = user.LockedUntilUtc.HasValue && user.LockedUntilUtc.Value > DateTime.UtcNow;
        return Task.FromResult(isLocked);
    }

    public async Task<List<User>> GetAllUsersAsync(CancellationToken cancellationToken)
    {
        return await _context.Users.ToListAsync(cancellationToken);
    }

    public async Task SetEnabledAsync(Guid userId, bool isEnabled, CancellationToken cancellationToken)
    {
        var user = await FindByIdAsync(userId, cancellationToken);
        if (user == null)
            throw new InvalidOperationException($"User {userId} not found.");

        user.IsEnabled = isEnabled;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var eventType = isEnabled ? "AccountEnabled" : "AccountDisabled";
        LogUserEnabledStateChanged(eventType, userId, isEnabled);
    }

    public async Task SetCanLoginToUIAsync(Guid userId, bool canLoginToUI, CancellationToken cancellationToken)
    {
        var user = await FindByIdAsync(userId, cancellationToken);
        if (user == null)
            throw new InvalidOperationException($"User {userId} not found.");

        user.CanLoginToUI = canLoginToUI;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var eventType = canLoginToUI ? "UIAccessGranted" : "UIAccessRevoked";
        LogUserUIAccessChanged(eventType, userId, canLoginToUI);
    }

    public async Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await FindByIdAsync(userId, cancellationToken);
        return user?.IsAdmin == true;
    }

    public async Task SetAdminAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var user = await FindByIdAsync(userId, cancellationToken);
        if (user == null)
            throw new InvalidOperationException($"User {userId} not found.");

        user.IsAdmin = isAdmin;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var eventType = isAdmin ? "AdminGranted" : "AdminRevoked";
        LogUserAdminStateChanged(eventType, userId, isAdmin);
    }

    public async Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await FindByIdAsync(userId, cancellationToken);
        if (user == null)
            throw new InvalidOperationException($"User {userId} not found.");

        if (user.IsEnabled)
            throw new InvalidOperationException($"User {userId} must be disabled before deletion.");

        var username = user.Username;
        _context.Users.Remove(user);
        await _context.SaveChangesAsync(cancellationToken);

        LogUserDeleted("AccountDeleted", username, userId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit: {EventType} - Created Entra user {Username} with ID {UserId}")]
    private partial void LogEntraUserCreated(string eventType, string username, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit: {EventType} - Created local user {Username} with ID {UserId} by {CreatedBy}")]
    private partial void LogLocalUserCreated(string eventType, string username, Guid userId, Guid? createdBy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit: {EventType} - Password updated for user {UserId}")]
    private partial void LogPasswordUpdated(string eventType, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit: {EventType} - User {UserId} locked out until {LockedUntil} after {Attempts} failed attempts")]
    private partial void LogUserLockedOut(string eventType, Guid userId, DateTime? lockedUntil, int attempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit: {EventType} - User {UserId} enabled state set to {IsEnabled}")]
    private partial void LogUserEnabledStateChanged(string eventType, Guid userId, bool isEnabled);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit: {EventType} - User {UserId} web UI access set to {CanLoginToUI}")]
    private partial void LogUserUIAccessChanged(string eventType, Guid userId, bool canLoginToUI);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit: {EventType} - User {UserId} admin state set to {IsAdmin}")]
    private partial void LogUserAdminStateChanged(string eventType, Guid userId, bool isAdmin);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit: {EventType} - User {Username} ({UserId}) was deleted")]
    private partial void LogUserDeleted(string eventType, string username, Guid userId);
}
