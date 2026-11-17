using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Cran;

/// <summary>
/// The source packages of a CRAN feed (<see cref="FeedType.Cran"/>): those uploaded to it, combined
/// with those of its mirrors. Names are matched regardless of case, versions by their normalized form.
/// </summary>
public interface ICranPackageService
{
    /// <summary>
    /// Writes the feed's <c>PACKAGES</c> index: the entry of the current version of each package,
    /// which is the newest listed version of the feed or the mirror's current version, whichever
    /// is newer. A version of the feed, listed or not, hides the mirror's version of the same
    /// normalized form.
    /// </summary>
    /// <returns>The index, in the DCF format.</returns>
    Task<string> GetIndexAsync(Feed feed, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the file names of a package's archive: its versions other than the current one, the
    /// feed's listed versions and the mirror's, oldest first.
    /// </summary>
    /// <returns>The file names, or null if neither the feed nor its mirrors have the package.</returns>
    Task<IReadOnlyList<string>> ListArchiveOrNullAsync(Feed feed, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a source package by its file name, <c>{name}_{version}.tar.gz</c>, and counts the
    /// download. A version that only a mirror has is downloaded from it and stored in the feed first.
    /// </summary>
    /// <returns>The package, or null if the version does not exist.</returns>
    Task<Stream> GetPackageStreamOrNullAsync(Feed feed, string fileName, CancellationToken cancellationToken);

    /// <summary>
    /// Uploads a source package to the feed. Its name and version are read from its <c>DESCRIPTION</c> file.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="fileName">The file name it was uploaded as, which must name the same package and version, or null.</param>
    /// <param name="archive">The package, read to its end.</param>
    /// <param name="maxBytes">The largest package the feed accepts.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<CranUploadResult> UploadAsync(Feed feed, string fileName, Stream archive, long maxBytes, CancellationToken cancellationToken);
}
