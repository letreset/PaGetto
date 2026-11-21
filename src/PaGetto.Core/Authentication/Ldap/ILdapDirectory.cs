using System.Threading;
using System.Threading.Tasks;

namespace PaGetto.Core.Authentication.Ldap;

/// <summary>
/// Checks usernames and passwords against the LDAP directory of <c>Authentication:Ldap</c>.
/// </summary>
public interface ILdapDirectory
{
    /// <summary>
    /// Finds the user by the name typed at sign-in and binds as it with <paramref name="password"/>.
    /// </summary>
    /// <returns>The user, or null when it doesn't exist or the password is wrong.</returns>
    /// <exception cref="System.Exception">The directory can't be reached or refused the search.</exception>
    Task<LdapUser> AuthenticateAsync(string username, string password, CancellationToken cancellationToken);
}
