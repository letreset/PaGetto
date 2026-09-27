using System;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Protocol.Models;
using PaGetto.Protocol.Search;

// ReSharper disable once CheckNamespace
namespace PaGetto.Protocol;

public partial class NuGetClientFactory
{
    private class SearchClient : ISearchClient
    {
        private readonly NuGetClientFactory _clientfactory;

        public SearchClient(NuGetClientFactory clientFactory)
        {
            _clientfactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        public async Task<SearchResponse> SearchAsync(
            string query = null,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            CancellationToken cancellationToken = default)
        {
            // TODO: Support search failover.
            var client = await _clientfactory.GetSearchClientAsync(cancellationToken);

            return await client.SearchAsync(query, skip, take, includePrerelease, includeSemVer2, cancellationToken);
        }
    }
}
