using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using NuGet.Versioning;

namespace PaGetto.Core.Upstream.Clients;

/// <summary>
/// Caches a feed's upstream package listings (versions and metadata) for a short time, so that
/// repeated restores don't query the upstream for every request. Downloads are not cached: once a
/// version is downloaded it is served from local storage.
/// </summary>
public class CachingUpstreamClient : IUpstreamClient
{
    private readonly IUpstreamClient _inner;
    private readonly UpstreamListingCache _cache;
    private readonly Guid _feedId;
    private readonly long _feedVersion;
    private readonly TimeSpan _duration;

    public CachingUpstreamClient(IUpstreamClient inner, UpstreamListingCache cache, Feed feed, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(feed);

        _inner = inner;
        _cache = cache;
        _feedId = feed.Id;
        // Saving a feed (including its mirrors) updates UpdatedAtUtc. Keying on it means entries
        // cached before a settings change are never read again, on every instance, and simply
        // expire.
        _feedVersion = feed.UpdatedAtUtc.Ticks;
        _duration = duration;
    }

    public Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string id, CancellationToken cancellationToken)
    {
        return _cache.GetOrListAsync(
            Key("versions", id),
            _duration,
            ct => _inner.ListPackageVersionsAsync(id, ct),
            cancellationToken);
    }

    public Task<IReadOnlyList<Package>> ListPackagesAsync(string id, CancellationToken cancellationToken)
    {
        return _cache.GetOrListAsync(
            Key("packages", id),
            _duration,
            ct => _inner.ListPackagesAsync(id, ct),
            cancellationToken);
    }

    public Task<Stream> DownloadPackageOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return _inner.DownloadPackageOrNullAsync(id, version, cancellationToken);
    }

    public string GetServiceIndexUrl()
    {
        return _inner.GetServiceIndexUrl();
    }

    private (string, Guid, long, string) Key(string kind, string id)
    {
        return (kind, _feedId, _feedVersion, id.ToLowerInvariant());
    }
}
