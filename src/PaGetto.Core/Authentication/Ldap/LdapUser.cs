using System.Collections.Generic;

namespace PaGetto.Core.Authentication.Ldap;

/// <summary>
/// A directory user whose password was checked.
/// </summary>
/// <param name="Id">The permanent id (<c>entryUUID</c>, <c>objectGUID</c>, ...).</param>
/// <param name="Dn">The distinguished name.</param>
/// <param name="Username">The PaGetto username.</param>
/// <param name="DisplayName">The display name, or null.</param>
/// <param name="Email">The email address, or null.</param>
/// <param name="Groups">The common name and the DN of each of the user's groups.</param>
public record LdapUser(
    string Id,
    string Dn,
    string Username,
    string DisplayName,
    string Email,
    IReadOnlyList<string> Groups);
