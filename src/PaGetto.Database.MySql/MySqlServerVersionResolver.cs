using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using PaGetto.Core.Configuration;
using Microsoft.EntityFrameworkCore;

namespace PaGetto.Database.MySql;

/// <summary>
/// Resolves the MySQL server version for <see cref="MySqlContext"/>.
/// <see cref="ServerVersion.AutoDetect(string)"/> opens a connection on every call, and the context is
/// configured once per request, so the detected version is cached per connection string.
/// </summary>
public static class MySqlServerVersionResolver
{
    private static readonly ConcurrentDictionary<string, Lazy<ServerVersion>> _detectedVersions = new();

    public static ServerVersion Resolve(DatabaseOptions options)
    {
        return Resolve(options, ServerVersion.AutoDetect);
    }

    internal static ServerVersion Resolve(DatabaseOptions options, Func<string, ServerVersion> autoDetect)
    {
        if (!string.IsNullOrWhiteSpace(options.ServerVersion))
        {
            return ServerVersion.Parse(options.ServerVersion);
        }

        var connectionString = options.ConnectionString;
        var detected = _detectedVersions.GetOrAdd(
            connectionString,
            key => new Lazy<ServerVersion>(() => autoDetect(key)));

        try
        {
            return detected.Value;
        }
        catch
        {
            // Don't cache a failure (e.g. the server was briefly unreachable): the next context retries.
            _detectedVersions.TryRemove(new KeyValuePair<string, Lazy<ServerVersion>>(connectionString, detected));
            throw;
        }
    }
}
