using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Search;

/// <summary>
/// A no-op indexer, used when search does not need to index packages.
/// </summary>
public class NullSearchIndexer : ISearchIndexer
{
    public Task IndexAsync(Package package, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
