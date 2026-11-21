using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Authentication.Ldap;

/// <summary>
/// Signs directory users in with their LDAP username and password, in the web UI and from client
/// tools.
/// </summary>
public interface ILdapAuthenticationService
{
    /// <summary>
    /// Checks the username and password against the directory, or against a recent successful
    /// sign-in, and creates or updates the user's account and groups.
    /// </summary>
    /// <returns>
    /// The account, which may be disabled; or null when LDAP is off, the user or password is wrong,
    /// the directory can't be reached or the username belongs to another account.
    /// </returns>
    Task<User> AuthenticateAsync(string username, string password, CancellationToken cancellationToken);
}
