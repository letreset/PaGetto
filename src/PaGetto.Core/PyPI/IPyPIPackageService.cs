using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.PyPI;

/// <summary>
/// The Python packages of a PyPI feed (<see cref="FeedType.PyPI"/>): the files uploaded to it,
/// combined with those of its mirrors. A project is an <see cref="Artifact"/> with its normalized
/// name, and its wheels and sdists are <see cref="ArtifactFile"/> rows of their version. The core
/// metadata of a wheel is stored next to it, as a file named after it with a <c>.metadata</c> suffix.
/// </summary>
public interface IPyPIPackageService
{
    /// <summary>
    /// Lists the projects that have files in the feed. Projects that only a mirror has are not
    /// listed: a mirror such as PyPI has too many.
    /// </summary>
    /// <returns>The normalized names, in order.</returns>
    Task<IReadOnlyList<string>> ListProjectsAsync(Feed feed, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the files of a project: those of the versions published to the feed, and those of
    /// the versions of its mirror. A version published to the feed hides the same version of the
    /// mirror. Unlisted versions are yanked.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The normalized project name.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The project, or null if neither the feed nor its mirror has a file of it.</returns>
    Task<PyPIProject> GetProjectOrNullAsync(Feed feed, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a file of a version, or the metadata stored next to a wheel. A file that only the
    /// mirror has is downloaded from it and stored in the feed first.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The normalized project name.</param>
    /// <param name="version">The version.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="countDownload">Whether a download of a wheel or sdist is counted.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The file, or null if there is no such file.</returns>
    Task<PyPIDownload> DownloadOrNullAsync(Feed feed, string name, string version, string fileName, bool countDownload, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a wheel or sdist uploaded with the legacy upload API. The version is created with
    /// the core metadata of its first file.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="upload">The upload.</param>
    /// <param name="maxBytes">The most the file may have.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<PyPIUploadResult> UploadAsync(Feed feed, PyPIUpload upload, long maxBytes, CancellationToken cancellationToken);
}
