using System;
using System.Net.Http;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Upstream;

public interface IUpstreamClientFactory
{
    /// <summary>
    /// Creates the client for the mirrors of a NuGet feed.
    /// </summary>
    IUpstreamClient CreateForFeed(Feed feed);

    /// <summary>
    /// Creates an HTTP client for one mirror of a feed, with the mirror's timeout. Used by the
    /// upstream clients of the other feed types.
    /// </summary>
    /// <param name="mirror">The mirror the client is for.</param>
    /// <param name="feedId">The feed the mirror belongs to.</param>
    /// <param name="authenticate">
    /// Whether requests carry the mirror's authentication headers. Pass false for URLs outside the mirror.
    /// </param>
    HttpClient CreateHttpClient(MirrorOptions mirror, Guid feedId, bool authenticate);
}
