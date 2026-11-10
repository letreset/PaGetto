using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Pub;

/// <summary>
/// The packages of a pub feed (<see cref="FeedType.Pub"/>): those published to it, combined with
/// those of its mirrors.
/// </summary>
public interface IPubPackageService
{
    /// <summary>
    /// Finds a package with all of its versions. A version published to the feed hides the same
    /// version of a mirror.
    /// </summary>
    /// <returns>The package, or null if neither the feed nor its mirrors have it.</returns>
    Task<PubPackage> FindPackageOrNullAsync(Feed feed, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a version's archive and counts the download. A version that only a mirror has is
    /// downloaded from it and stored in the feed first.
    /// </summary>
    /// <returns>The archive, or null if the version does not exist.</returns>
    Task<Stream> GetArchiveStreamOrNullAsync(Feed feed, string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a package archive to the feed.
    /// </summary>
    /// <param name="feed">The feed to publish to.</param>
    /// <param name="archive">The gzipped tar archive. It must be seekable.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<PubPublishResult> PublishAsync(Feed feed, Stream archive, CancellationToken cancellationToken);
}
