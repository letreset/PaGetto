using PaGetto.Core.Entities;

namespace PaGetto.Core.Upstream;

public interface IUpstreamClientFactory
{
    IUpstreamClient CreateForFeed(Feed feed);
}
