using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;

namespace PaGetto.Core.Artifacts;

/// <summary>
/// The process-wide, in-memory store of indexes built from all of a feed's packages, such as the
/// RubyGems <c>/versions</c> file, the Hex <c>/names</c> file, a CRAN <c>PACKAGES</c> file or a
/// VSIX gallery, which clients ask for on every install and which are costly to build.
/// </summary>
/// <remarks>
/// An index is cached under the feed's <see cref="Feed.ArtifactRevision"/> and
/// <see cref="Feed.UpdatedAtUtc"/>, which the request read with the feed, so a changed package or
/// changed feed settings make the next request build it again, on every replica. Entries also
/// expire: some indexes include what the feed's mirrors list, or download counts.
/// </remarks>
public sealed class ArtifactIndexCache : IDisposable
{
    /// <summary>
    /// How long an index that has nothing from the feed's mirrors is kept.
    /// </summary>
    public static readonly TimeSpan LocalIndexDuration = TimeSpan.FromMinutes(5);

    // Each entry's size is its length in bytes.
    private const long SizeLimit = 256L * 1024 * 1024;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = SizeLimit });

    /// <summary>
    /// How long an index that includes what the feed's mirrors list is kept: as long as those
    /// listings are, or <see cref="LocalIndexDuration"/> when the feed has no mirrors.
    /// </summary>
    public static TimeSpan GetMirroredIndexDuration(IFeedSettingsResolver feedSettings, Feed feed)
    {
        ArgumentNullException.ThrowIfNull(feedSettings);

        return feedSettings.GetMirrorOptions(feed).Count == 0
            ? LocalIndexDuration
            : feedSettings.GetUpstreamListingCacheDuration(feed);
    }

    /// <summary>
    /// Returns the cached index named <paramref name="index"/> of <paramref name="feed"/>, or builds
    /// and caches it for <paramref name="duration"/>. A duration of zero or less builds it every time.
    /// </summary>
    /// <param name="feed">The feed, as read for the current request.</param>
    /// <param name="index">
    /// The index's name, with whatever else the content depends on, such as the URL its links are built from.
    /// </param>
    /// <param name="duration">How long the index is kept at most.</param>
    /// <param name="build">Builds the index.</param>
    public async Task<byte[]> GetOrBuildAsync(Feed feed, string index, TimeSpan duration, Func<Task<byte[]>> build)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(build);

        if (duration <= TimeSpan.Zero)
        {
            return await build();
        }

        var key = (feed.Id, index, feed.ArtifactRevision, feed.UpdatedAtUtc);
        if (_cache.TryGetValue(key, out byte[] cached))
        {
            return cached;
        }

        // Concurrent misses each build the index: the build reads the database through the
        // request's own context, which can't be shared with requests that outlive it.
        var content = await build();

        _cache.Set(key, content, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = duration,
            Size = content.Length,
        });

        return content;
    }

    public void Dispose()
    {
        _cache.Dispose();
    }
}
