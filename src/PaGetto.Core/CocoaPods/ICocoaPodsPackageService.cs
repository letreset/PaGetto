using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.CocoaPods;

/// <summary>
/// The pods of a CocoaPods feed (<see cref="FeedType.CocoaPods"/>): those published to it, combined
/// with those of its mirrors. A pod is an <see cref="Artifact"/> under its escaped name (see
/// <see cref="CocoaPodsPodName.Escape"/>), and a version's podspec is its manifest. A version's source
/// archive, when it was published with one, is the version's archive. An unlisted version is left
/// out of the index, but its podspec is still served.
/// </summary>
public interface ICocoaPodsPackageService
{
    /// <summary>
    /// The index file of a shard (<c>all_pods_versions_d_a_2.txt</c>): the pods of the shard with their
    /// listed versions, those of the feed and those of its mirrors. A version of the feed, listed or
    /// not, hides the mirror's version of the same text.
    /// </summary>
    /// <returns>The file, which is empty when neither the feed nor its mirrors have a pod in the shard.</returns>
    Task<string> GetIndexAsync(Feed feed, string[] shard, CancellationToken cancellationToken);

    /// <summary>
    /// <c>all_pods.txt</c>: the names of the pods stored in the feed. Pods that only a mirror has are
    /// not listed: a mirror such as cdn.cocoapods.org has too many.
    /// </summary>
    Task<string> GetAllPodsAsync(Feed feed, CancellationToken cancellationToken);

    /// <summary>
    /// The podspec of a version, as CocoaPods downloads it. A version that only a mirror has is
    /// downloaded from it and stored in the feed first.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The pod name, in its exact case.</param>
    /// <param name="version">The version.</param>
    /// <param name="sourcesUrl">The absolute URL the source archives of the feed are under.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The podspec as JSON, or null if neither the feed nor its mirrors have the version.</returns>
    Task<string> GetPodspecOrNullAsync(Feed feed, string name, string version, string sourcesUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the source archive of a version that was published with one, and counts the download.
    /// </summary>
    /// <returns>The archive, or null if the version does not exist or has no archive of that file name.</returns>
    Task<Stream> GetSourceStreamOrNullAsync(Feed feed, string name, string version, string fileName, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a version: its podspec, and optionally its source archive, which the podspec then
    /// points at.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="podspec">The podspec in its JSON form.</param>
    /// <param name="source">The source archive, read to its end, or null.</param>
    /// <param name="sourceFileName">The file name of the source archive, which gives its type.</param>
    /// <param name="maxBytes">The largest archive the feed accepts.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<CocoaPodsPublishResult> PublishAsync(Feed feed, string podspec, Stream source, string sourceFileName, long maxBytes, CancellationToken cancellationToken);
}
