using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Authentication.Ldap;

public partial class LdapAuthenticationService : ILdapAuthenticationService
{
    private readonly ILdapDirectory _directory;
    private readonly LdapSignInCache _cache;
    private readonly ExternalAccountService _accounts;
    private readonly IUserService _userService;
    private readonly NugetAuthenticationOptions _options;
    private readonly ILogger<LdapAuthenticationService> _logger;

    public LdapAuthenticationService(
        ILdapDirectory directory,
        LdapSignInCache cache,
        ExternalAccountService accounts,
        IUserService userService,
        IOptionsSnapshot<NugetAuthenticationOptions> options,
        ILogger<LdapAuthenticationService> logger)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<User> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
    {
        if (!_options.IsLdapEnabled || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return null;

        if (_cache.TryGetUserId(username, password, out var cachedUserId))
        {
            var cachedUser = await _userService.FindByIdAsync(cachedUserId, cancellationToken);
            if (cachedUser?.AuthProvider == AuthProvider.Ldap)
                return cachedUser;
        }

        LdapUser entry;
        try
        {
            entry = await _directory.AuthenticateAsync(username, password, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDirectoryFailed(ex, username);
            return null;
        }

        if (entry == null)
        {
            LogDirectorySignInFailed(username);
            return null;
        }

        User user;
        try
        {
            user = await _accounts.UpsertAsync(
                AuthProvider.Ldap,
                entry.Id,
                entry.Username,
                entry.DisplayName ?? entry.Username,
                entry.Email,
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            // Logged by the account service: the username belongs to another account.
            return null;
        }

        if (!user.IsEnabled)
            return user;

        await _accounts.SyncRolesAsync(user, entry.Groups, cancellationToken);
        _cache.Set(username, password, user.Id, TimeSpan.FromSeconds(_options.Ldap.CacheSeconds));

        return user;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "LDAP sign-in of {Username} failed: the directory could not be searched")]
    private partial void LogDirectoryFailed(Exception exception, string username);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LDAP sign-in of {Username} failed: no such directory user or a wrong password")]
    private partial void LogDirectorySignInFailed(string username);
}
