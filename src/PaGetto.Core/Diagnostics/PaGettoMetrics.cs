using System.Collections.Generic;
using System.Diagnostics.Metrics;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Diagnostics;

/// <summary>
/// The metrics of PaGetto itself, in the <c>PaGetto</c> meter. ASP.NET Core, HttpClient (upstream
/// requests), the runtime and EF Core have metrics of their own, which the host exports next to these.
/// </summary>
public static class PaGettoMetrics
{
    public const string MeterName = "PaGetto";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> UpstreamListingLookups = Meter.CreateCounter<long>(
        "pagetto.upstream.listing.lookups",
        unit: "{lookup}",
        description: "Lookups of upstream package listings, by whether the listing cache had them (pagetto.cache.result: hit or miss).");

    private static readonly Counter<long> UpstreamFailures = Meter.CreateCounter<long>(
        "pagetto.upstream.failures",
        unit: "{failure}",
        description: "Mirror requests that failed: an error, or an answer other than success or 404.");

    private static readonly Counter<long> IndexCacheLookups = Meter.CreateCounter<long>(
        "pagetto.index.lookups",
        unit: "{lookup}",
        description: "Lookups of indexes built from a whole feed, by whether the index cache had them (pagetto.cache.result: hit or miss).");

    private static readonly Counter<long> PackagesStored = Meter.CreateCounter<long>(
        "pagetto.packages.stored",
        unit: "{version}",
        description: "Package versions stored, by feed type and by whether they were published or mirrored (pagetto.package.source).");

    private static readonly Counter<long> PackagesRemoved = Meter.CreateCounter<long>(
        "pagetto.packages.removed",
        unit: "{version}",
        description: "Package versions the retention cleanup removed, by reason (pagetto.removal.reason: retention or unused_mirror).");

    public static void RecordUpstreamListingLookup(bool hit)
    {
        UpstreamListingLookups.Add(1, CacheResult(hit));
    }

    public static void RecordUpstreamFailure()
    {
        UpstreamFailures.Add(1);
    }

    public static void RecordIndexLookup(bool hit)
    {
        IndexCacheLookups.Add(1, CacheResult(hit));
    }

    public static void RecordPackageStored(FeedType feedType, bool mirrored)
    {
        PackagesStored.Add(
            1,
            new KeyValuePair<string, object>("pagetto.feed.type", feedType.ToString()),
            new KeyValuePair<string, object>("pagetto.package.source", mirrored ? "mirrored" : "published"));
    }

    public static void RecordPackagesRemoved(int count, bool unusedMirror)
    {
        if (count > 0)
        {
            PackagesRemoved.Add(count, new KeyValuePair<string, object>("pagetto.removal.reason", unusedMirror ? "unused_mirror" : "retention"));
        }
    }

    private static KeyValuePair<string, object> CacheResult(bool hit)
    {
        return new KeyValuePair<string, object>("pagetto.cache.result", hit ? "hit" : "miss");
    }
}
