using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace PaGetto.Core.Authentication.Ldap;

/// <summary>
/// Remembers successful LDAP sign-ins for a short time, so a client tool's many requests don't each
/// bind to the directory. Entries are keyed by a keyed hash of the username and password, with a
/// key made for this process, so the cache never holds a password.
/// </summary>
public sealed class LdapSignInCache : IDisposable
{
    private const long SizeLimit = 10_000;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = SizeLimit });
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Finds the account a recent sign-in with this username and password belongs to.
    /// </summary>
    public bool TryGetUserId(string username, string password, out Guid userId)
    {
        return _cache.TryGetValue(GetKey(username, password), out userId);
    }

    /// <summary>
    /// Remembers that this username and password signed in to the account for <paramref name="duration"/>.
    /// </summary>
    public void Set(string username, string password, Guid userId, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return;

        _cache.Set(GetKey(username, password), userId, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = duration,
            Size = 1,
        });
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    private string GetKey(string username, string password)
    {
        var data = Encoding.UTF8.GetBytes(username.ToUpperInvariant() + "\0" + password);
        return Convert.ToBase64String(HMACSHA256.HashData(_key, data));
    }
}
