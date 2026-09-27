using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;

namespace PaGetto.Core.Upstream;

/// <summary>
/// The process-wide, in-memory store behind <see cref="Clients.CachingUpstreamClient"/>. It owns its
/// own <see cref="MemoryCache"/> so its size limit neither applies to nor breaks other users of
/// the app's shared <see cref="IMemoryCache"/>.
/// </summary>
public sealed class UpstreamListingCache : IDisposable
{
    // Each entry's size is the number of versions or packages it holds, so the limit bounds the
    // number of cached listing items across all feeds rather than the number of package ids.
    private const long SizeLimit = 100_000;

    private readonly MemoryCache _cache;

    // The upstream call currently running for each key, shared by every caller that misses the
    // cache meanwhile, so a burst of requests for one package queries the upstream once.
    private readonly ConcurrentDictionary<object, Lazy<Task>> _inFlight = new();

    public UpstreamListingCache()
        : this(clock: null)
    {
    }

    internal UpstreamListingCache(ISystemClock clock)
    {
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            Clock = clock,
            SizeLimit = SizeLimit,
        });
    }

    /// <summary>
    /// Returns the cached listing for <paramref name="key"/>, or runs <paramref name="list"/> and
    /// caches its result for <paramref name="duration"/>. Concurrent callers for the same key share
    /// one run of <paramref name="list"/>. Empty listings are not cached: upstream clients return an
    /// empty list both for unknown packages and for failed requests.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="duration">How long a non-empty listing is cached.</param>
    /// <param name="list">
    /// Queries the upstream. It gets <see cref="CancellationToken.None"/>, because it is shared by
    /// several callers and one of them leaving must not cancel it for the others.
    /// </param>
    /// <param name="cancellationToken">Stops this caller from waiting, without cancelling the shared call.</param>
    public async Task<IReadOnlyList<T>> GetOrListAsync<T>(
        object key,
        TimeSpan duration,
        Func<CancellationToken, Task<IReadOnlyList<T>>> list,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(key, out IReadOnlyList<T> cached))
            return cached;

        var call = _inFlight.GetOrAdd(key, k => new Lazy<Task>(() => ListAndCacheAsync(k, duration, list)));

        return await ((Task<IReadOnlyList<T>>)call.Value).WaitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<T>> ListAndCacheAsync<T>(
        object key,
        TimeSpan duration,
        Func<CancellationToken, Task<IReadOnlyList<T>>> list)
    {
        try
        {
            // A call for this key may have completed and been cached between this caller's cache
            // miss and its registration as the in-flight call.
            if (_cache.TryGetValue(key, out IReadOnlyList<T> cached))
                return cached;

            var result = await list(CancellationToken.None);

            if (result.Count > 0)
            {
                _cache.Set(key, result, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = duration,
                    Size = result.Count,
                });
            }

            return result;
        }
        finally
        {
            // Removed only after the result is cached, so a later caller either finds it in the
            // cache or starts a new call. Failures are not cached, so the next caller retries.
            _inFlight.TryRemove(key, out _);
        }
    }

    public void Dispose()
    {
        _cache.Dispose();
    }
}
