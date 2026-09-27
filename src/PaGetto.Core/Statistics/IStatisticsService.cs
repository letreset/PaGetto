using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PaGetto.Core.Statistics;

public interface IStatisticsService
{
    Task<int> GetPackagesTotalAmount(Guid feedId);
    Task<int> GetVersionsTotalAmount(Guid feedId);

    /// <summary>
    /// Returns the download, size and version statistics of a feed, with at most
    /// <paramref name="listSize"/> entries in each list.
    /// </summary>
    Task<FeedStatistics> GetFeedStatisticsAsync(Guid feedId, int listSize, CancellationToken cancellationToken);
    IEnumerable<string> GetKnownServices();
}
