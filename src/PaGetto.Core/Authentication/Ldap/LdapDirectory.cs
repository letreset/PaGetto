using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Novell.Directory.Ldap;
using PaGetto.Core.Configuration;

namespace PaGetto.Core.Authentication.Ldap;

/// <summary>
/// The <see cref="ILdapDirectory"/> of an LDAP server: binds with the search account, finds the
/// user and its groups, then binds as the user to check its password.
/// </summary>
public partial class LdapDirectory : ILdapDirectory
{
    private const string CommonNameAttribute = "cn";
    private const string ActiveDirectoryGuidAttribute = "objectGUID";

    private readonly IOptionsMonitor<NugetAuthenticationOptions> _options;
    private readonly ILogger<LdapDirectory> _logger;

    public LdapDirectory(IOptionsMonitor<NugetAuthenticationOptions> options, ILogger<LdapDirectory> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LdapUser> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
    {
        // Most servers treat a bind with an empty password as an anonymous bind, which succeeds.
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return null;

        var options = _options.CurrentValue.Ldap;
        var timeout = (int)TimeSpan.FromSeconds(options.TimeoutSeconds).TotalMilliseconds;

        var connectionOptions = new LdapConnectionOptions();
        if (options.Security == LdapSecurity.Ssl)
            connectionOptions.UseSsl();

        using var connection = new LdapConnection(connectionOptions) { ConnectionTimeout = timeout };
        var constraints = connection.Constraints;
        constraints.TimeLimit = timeout;
        connection.Constraints = constraints;

        await connection.ConnectAsync(options.Server, GetPort(options), cancellationToken);
        if (options.Security == LdapSecurity.StartTls)
            await connection.StartTlsAsync(cancellationToken);

        if (!string.IsNullOrEmpty(options.BindDn))
            await connection.BindAsync(options.BindDn, options.BindPassword, cancellationToken);

        var entries = await SearchAsync(
            connection,
            options.SearchBase,
            string.Format(CultureInfo.InvariantCulture, options.UserFilter, EscapeFilterValue(username)),
            [options.UsernameAttribute, options.IdAttribute, options.DisplayNameAttribute, CommonNameAttribute, options.EmailAttribute, options.GroupAttribute],
            maxResults: 2,
            timeout,
            cancellationToken);

        if (entries.Count != 1)
        {
            if (entries.Count > 1)
                LogAmbiguousUser(username);
            return null;
        }

        var entry = entries[0];
        var id = GetId(entry, options.IdAttribute);
        var directoryUsername = entry.GetStringValueOrDefault(options.UsernameAttribute, null);
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(directoryUsername))
        {
            LogMissingAttribute(entry.Dn, string.IsNullOrEmpty(id) ? options.IdAttribute : options.UsernameAttribute);
            return null;
        }

        List<string> groupDns;
        if (string.IsNullOrEmpty(options.GroupSearchFilter))
        {
            groupDns = GetValues(entry, options.GroupAttribute);
        }
        else
        {
            var groups = await SearchAsync(
                connection,
                string.IsNullOrEmpty(options.GroupSearchBase) ? options.SearchBase : options.GroupSearchBase,
                string.Format(CultureInfo.InvariantCulture, options.GroupSearchFilter, EscapeFilterValue(entry.Dn)),
                [CommonNameAttribute],
                maxResults: 0,
                timeout,
                cancellationToken);
            groupDns = groups.Select(group => group.Dn).ToList();
        }

        try
        {
            await connection.BindAsync(entry.Dn, password, cancellationToken);
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.InvalidCredentials)
        {
            return null;
        }

        var displayName = entry.GetStringValueOrDefault(options.DisplayNameAttribute, null)
                          ?? entry.GetStringValueOrDefault(CommonNameAttribute, null);

        return new LdapUser(
            id,
            entry.Dn,
            directoryUsername,
            displayName,
            entry.GetStringValueOrDefault(options.EmailAttribute, null),
            GetGroupNames(groupDns));
    }

    /// <summary>
    /// Escapes a value for an LDAP search filter (RFC 4515), so a typed name can't change the filter.
    /// </summary>
    public static string EscapeFilterValue(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': builder.Append("\\5c"); break;
                case '*': builder.Append("\\2a"); break;
                case '(': builder.Append("\\28"); break;
                case ')': builder.Append("\\29"); break;
                case '\0': builder.Append("\\00"); break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// The names a group can be linked by: the value of its first RDN (its common name) and its DN.
    /// </summary>
    public static IReadOnlyList<string> GetGroupNames(IEnumerable<string> groupDns)
    {
        var names = new List<string>();
        foreach (var dn in groupDns)
        {
            var rdns = LdapDn.ExplodeDn(dn, true);
            if (rdns is { Length: > 0 } && !string.IsNullOrEmpty(rdns[0]))
                names.Add(rdns[0]);
            names.Add(dn);
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int GetPort(LdapOptions options)
    {
        if (options.Port > 0)
            return options.Port;

        return options.Security == LdapSecurity.Ssl ? LdapConnection.DefaultSslPort : LdapConnection.DefaultPort;
    }

    private static async Task<List<LdapEntry>> SearchAsync(
        LdapConnection connection,
        string searchBase,
        string filter,
        string[] attributes,
        int maxResults,
        int timeout,
        CancellationToken cancellationToken)
    {
        var constraints = connection.SearchConstraints;
        constraints.MaxResults = maxResults;
        constraints.TimeLimit = timeout;

        var results = await connection.SearchAsync(
            searchBase,
            LdapConnection.ScopeSub,
            filter,
            attributes.Where(a => !string.IsNullOrEmpty(a)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            false,
            constraints,
            cancellationToken);

        var entries = new List<LdapEntry>();
        try
        {
            while (await results.HasMoreAsync(cancellationToken))
                entries.Add(await results.NextAsync(cancellationToken));
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.SizeLimitExceeded)
        {
            // More entries than maxResults: what was read so far tells the caller enough.
        }

        return entries;
    }

    private static string GetId(LdapEntry entry, string idAttribute)
    {
        if (string.Equals(idAttribute, ActiveDirectoryGuidAttribute, StringComparison.OrdinalIgnoreCase))
        {
            var bytes = entry.GetBytesValueOrDefault(idAttribute, null);
            return bytes is { Length: 16 } ? new Guid(bytes).ToString() : null;
        }

        return entry.GetStringValueOrDefault(idAttribute, null);
    }

    private static List<string> GetValues(LdapEntry entry, string attribute)
    {
        if (string.IsNullOrEmpty(attribute))
            return [];

        var value = entry.GetOrDefault(attribute, null);
        return value?.StringValueArray.ToList() ?? [];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "LDAP sign-in refused: more than one directory entry matches the username {Username}")]
    private partial void LogAmbiguousUser(string username);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LDAP sign-in refused: the directory entry {Dn} has no {Attribute} attribute")]
    private partial void LogMissingAttribute(string dn, string attribute);
}
