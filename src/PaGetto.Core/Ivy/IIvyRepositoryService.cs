using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using PaGetto.Core.Maven;

namespace PaGetto.Core.Ivy;

/// <summary>
/// The Ivy repository of an Ivy feed (<see cref="FeedType.Ivy"/>): the files published to it,
/// combined with those of its mirrors. Each revision is an <see cref="Artifact"/> version named as
/// <see cref="IvyPath.GetPackageName"/> says, with its files as <see cref="ArtifactFile"/> rows named
/// by their path below the revision's folder, e.g. <c>jars/sbt-tool.jar</c>.
/// </summary>
public interface IIvyRepositoryService
{
    /// <summary>
    /// Serves a file of a revision or its checksum. A file of a revision that only a mirror has is
    /// downloaded from it and stored in the feed first; snapshots are not mirrored.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="path">The path of a file.</param>
    /// <param name="countDownload">Whether a download of the revision's main artifact is counted.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The content, or null if the file does not exist.</returns>
    Task<MavenContent> GetAsync(Feed feed, IvyPath path, bool countDownload, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the revisions of a module: the feed's listed revisions and those its mirrors list.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="path">The path of a module's folder.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The revisions, lowest first. Empty if neither the feed nor a mirror has the module.</returns>
    Task<IReadOnlyList<string>> ListRevisionsAsync(Feed feed, IvyPath path, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a file uploaded to a path, as <c>sbt publish</c> does with an Ivy-style resolver. An
    /// uploaded checksum is checked against the stored file, and an uploaded <c>ivy.xml</c> must be
    /// the descriptor of its path's revision.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="path">The path of a file.</param>
    /// <param name="content">The uploaded content.</param>
    /// <param name="maxBytes">The most a file may have.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<MavenPutResult> PutAsync(Feed feed, IvyPath path, Stream content, long maxBytes, CancellationToken cancellationToken);
}
