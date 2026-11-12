using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Maven;

/// <summary>
/// The Maven repository of a Maven feed (<see cref="FeedType.Maven"/>): the files published to it,
/// combined with those of its mirrors. Each version is an <see cref="Artifact"/> version named
/// <c>groupId:artifactId</c>, with its files as <see cref="ArtifactFile"/> rows.
/// </summary>
public interface IMavenRepositoryService
{
    /// <summary>
    /// Serves a path of the repository: a file, its checksum, or metadata that the server writes
    /// from the versions and files it has. A file of a release version that only a mirror has is
    /// downloaded from it and stored in the feed first.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="path">The path.</param>
    /// <param name="countDownload">Whether a download of the version's main file is counted.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The content, or null if the path does not exist.</returns>
    Task<MavenContent> GetAsync(Feed feed, MavenPath path, bool countDownload, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a file uploaded to a path, as <c>mvn deploy</c> and Gradle's <c>maven-publish</c> do.
    /// An uploaded checksum is checked against the stored file, and uploaded metadata is ignored,
    /// since the server writes its own.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="path">The path.</param>
    /// <param name="content">The uploaded content.</param>
    /// <param name="maxBytes">The most a file may have.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<MavenPutResult> PutAsync(Feed feed, MavenPath path, Stream content, long maxBytes, CancellationToken cancellationToken);
}
