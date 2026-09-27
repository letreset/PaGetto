using System;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaGetto.Core.Authentication;

public partial class FeedAuthenticationService : IFeedAuthenticationService
{
    private readonly IUserService _userService;
    private readonly ITokenService _tokenService;
    private readonly NugetAuthenticationOptions _authOptions;
    private readonly ILogger<FeedAuthenticationService> _logger;

    public FeedAuthenticationService(
        IUserService userService,
        ITokenService tokenService,
        IOptionsSnapshot<NugetAuthenticationOptions> authOptions,
        ILogger<FeedAuthenticationService> logger)
    {
        ArgumentNullException.ThrowIfNull(userService);
        ArgumentNullException.ThrowIfNull(tokenService);
        ArgumentNullException.ThrowIfNull(authOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _userService = userService;
        _tokenService = tokenService;
        _authOptions = authOptions.Value;
        _logger = logger;
    }

    public async Task<AuthResult> AuthenticateByTokenAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token))
            return new AuthResult(false, null, null);

        var pat = await _tokenService.ValidateTokenAsync(token, cancellationToken);
        if (pat == null)
        {
            LogTokenAuthenticationFailed("LoginFailure");
            return new AuthResult(false, null, null);
        }

        return new AuthResult(true, pat.UserId, pat.User.Username);
    }

    public async Task<AuthResult> AuthenticateByCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return new AuthResult(false, null, null);

        // NuGet clients send a PAT as the basic auth password. A password in PAT format is only
        // checked as a token, so a PAT never counts as a failed login against the local account
        // (which would lock the owner out after a few restores).
        if (password.StartsWith(TokenService.TokenPrefix, StringComparison.Ordinal))
        {
            var tokenResult = await AuthenticateByTokenAsync(password, cancellationToken);
            if (tokenResult.IsAuthenticated)
            {
                if (!string.Equals(tokenResult.Username, username, StringComparison.OrdinalIgnoreCase))
                {
                    LogPatUsernameMismatch("LoginFailure", username, tokenResult.Username);
                    return new AuthResult(false, null, null);
                }

                return tokenResult;
            }
        }
        else if (_authOptions.Mode is AuthenticationMode.Local or AuthenticationMode.Hybrid)
        {
            var localResult = await TryAuthenticateLocalAccountAsync(username, password, cancellationToken);
            if (localResult.IsAuthenticated)
                return localResult;
        }

        LogCredentialAuthenticationFailed("LoginFailure", username);
        return new AuthResult(false, null, null);
    }

    private async Task<AuthResult> TryAuthenticateLocalAccountAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByUsernameAsync(username, cancellationToken);
        if (user == null || user.AuthProvider != AuthProvider.Local)
            return new AuthResult(false, null, null);

        if (!user.IsEnabled)
        {
            LogDisabledAccountLoginAttempt("LoginFailure", username, user.Id);
            return new AuthResult(false, null, null);
        }

        if (await _userService.IsLockedOutAsync(user))
        {
            LogLockedOutAccountLoginAttempt("LoginFailure", username, user.Id);
            return new AuthResult(false, null, null);
        }

        var passwordValid = await _userService.VerifyPasswordAsync(user, password);
        if (!passwordValid)
        {
            await _userService.RecordFailedLoginAsync(user.Id, cancellationToken);
            LogLocalLoginFailed("LoginFailure", username, user.Id);
            return new AuthResult(false, null, null);
        }

        await _userService.ResetFailedLoginCountAsync(user.Id, cancellationToken);

        // The password is a default or temporary one until it's changed in the web UI.
        if (user.MustChangePassword)
        {
            LogPasswordChangeRequiredLoginAttempt("LoginFailure", username, user.Id);
            return new AuthResult(false, null, null);
        }

        LogLocalLoginSucceeded("LoginSuccess", username, user.Id);

        return new AuthResult(true, user.Id, user.Username);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit: {EventType} - Token authentication failed: invalid or expired token")]
    private partial void LogTokenAuthenticationFailed(string eventType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit: {EventType} - PAT username mismatch: supplied {SuppliedUsername}, token belongs to {TokenUsername}")]
    private partial void LogPatUsernameMismatch(string eventType, string suppliedUsername, string tokenUsername);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit: {EventType} - Credential authentication failed for username {Username}")]
    private partial void LogCredentialAuthenticationFailed(string eventType, string username);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit: {EventType} - Login attempt for disabled local account {Username} ({UserId})")]
    private partial void LogDisabledAccountLoginAttempt(string eventType, string username, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit: {EventType} - Login attempt for locked out local account {Username} ({UserId})")]
    private partial void LogLockedOutAccountLoginAttempt(string eventType, string username, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit: {EventType} - Local account {Username} ({UserId}) must change its password in the web UI before it can be used by NuGet clients")]
    private partial void LogPasswordChangeRequiredLoginAttempt(string eventType, string username, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit: {EventType} - Failed login attempt for local account {Username} ({UserId})")]
    private partial void LogLocalLoginFailed(string eventType, string username, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit: {EventType} - Local account {Username} ({UserId}) authenticated successfully")]
    private partial void LogLocalLoginSucceeded(string eventType, string username, Guid userId);
}
