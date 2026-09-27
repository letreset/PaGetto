using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace PaGetto.Core.Upstream.Clients;

/// <summary>
/// Combines several upstream package sources, in priority order. Version and metadata lists are
/// the union of every upstream (an earlier upstream wins for the same version), and packages are
/// downloaded from the first upstream that has them. A failing upstream is logged and skipped.
/// </summary>
public partial class FallbackUpstreamClient : IUpstreamClient
{
    private readonly IReadOnlyList<IUpstreamClient> _upstreams;
    private readonly ILogger<FallbackUpstreamClient> _logger;

    // The upstream that served the last successful download, so the package's CachedFrom
    // records where it actually came from.
    private IUpstreamClient _lastDownloadSource;

    public FallbackUpstreamClient(IReadOnlyList<IUpstreamClient> upstreams, ILogger<FallbackUpstreamClient> logger)
    {
        ArgumentNullException.ThrowIfNull(upstreams);
        ArgumentNullException.ThrowIfNull(logger);

        if (upstreams.Count == 0)
            throw new ArgumentException("At least one upstream is required.", nameof(upstreams));

        _upstreams = upstreams;
        _logger = logger;
    }

    public async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string id, CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(_upstreams.Select(
            upstream => ListOrEmptyAsync(upstream, u => u.ListPackageVersionsAsync(id, cancellationToken), id, cancellationToken)));

        // Distinct keeps the first occurrence, so the order follows upstream priority.
        return results.SelectMany(versions => versions).Distinct().ToList();
    }

    public async Task<IReadOnlyList<Package>> ListPackagesAsync(string id, CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(_upstreams.Select(
            upstream => ListOrEmptyAsync(upstream, u => u.ListPackagesAsync(id, cancellationToken), id, cancellationToken)));

        var seen = new HashSet<NuGetVersion>();
        var packages = new List<Package>();

        foreach (var package in results.SelectMany(p => p))
        {
            if (seen.Add(package.Version))
                packages.Add(package);
        }

        return packages;
    }

    public async Task<Stream> DownloadPackageOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        foreach (var upstream in _upstreams)
        {
            try
            {
                var stream = await upstream.DownloadPackageOrNullAsync(id, version, cancellationToken);
                if (stream != null)
                {
                    _lastDownloadSource = upstream;
                    return stream;
                }
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested)
            {
                LogDownloadFailed(e, id, version, upstream.GetServiceIndexUrl());
            }
        }

        return null;
    }

    public string GetServiceIndexUrl()
    {
        return (_lastDownloadSource ?? _upstreams[0]).GetServiceIndexUrl();
    }

    private async Task<IReadOnlyList<T>> ListOrEmptyAsync<T>(
        IUpstreamClient upstream,
        Func<IUpstreamClient, Task<IReadOnlyList<T>>> list,
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            return await list(upstream);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            LogListFailed(e, id, upstream.GetServiceIndexUrl());

            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to download {PackageId} {PackageVersion} from upstream {Upstream}, trying the next upstream")]
    private partial void LogDownloadFailed(Exception exception, string packageId, NuGetVersion packageVersion, string upstream);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to list {PackageId} from upstream {Upstream}, skipping it")]
    private partial void LogListFailed(Exception exception, string packageId, string upstream);
}
