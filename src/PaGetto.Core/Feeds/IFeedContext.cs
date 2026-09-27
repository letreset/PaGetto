using PaGetto.Core.Entities;

namespace PaGetto.Core.Feeds;

public interface IFeedContext
{
    Feed CurrentFeed { get; }
    bool IsDefaultRoute { get; }
}
