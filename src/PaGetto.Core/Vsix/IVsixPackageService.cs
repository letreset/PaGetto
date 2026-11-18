using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Vsix;

/// <summary>
/// The Visual Studio extensions of a VSIX feed (<see cref="FeedType.Vsix"/>): those uploaded to it,
/// combined with those of its mirrors. Identifiers are matched regardless of case, versions as
/// <see cref="VsixVersion"/> reads them.
/// </summary>
public interface IVsixPackageService
{
    /// <summary>
    /// Writes the feed's gallery: an Atom feed with the current version of each extension, which
    /// is the newest listed version of the feed or the mirror's version, whichever is newer. A
    /// version of the feed, listed or not, hides the mirror's version of the same number. The
    /// files and images are linked on this server, so Visual Studio needs no access to the mirrors.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="galleryUrl">The absolute URL of the feed's gallery, without a trailing slash.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<string> GetAtomFeedAsync(Feed feed, string galleryUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the VSIX file of a version and counts the download. A version that only a mirror has
    /// is downloaded from it and stored in the feed first.
    /// </summary>
    /// <returns>The file, or null if the version does not exist.</returns>
    Task<Stream> GetPackageStreamOrNullAsync(Feed feed, string id, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the icon or preview image of a version. The images of a version that only a mirror
    /// has are fetched from the mirror.
    /// </summary>
    /// <returns>The image, or null if the version or its image does not exist.</returns>
    Task<VsixImage> GetImageOrNullAsync(Feed feed, string id, string version, VsixImageKind kind, CancellationToken cancellationToken);

    /// <summary>
    /// Uploads a VSIX file to the feed. Its identifier and version are read from its manifest.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="vsix">The file, read to its end.</param>
    /// <param name="maxBytes">The largest file the feed accepts.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<VsixUploadResult> UploadAsync(Feed feed, Stream vsix, long maxBytes, CancellationToken cancellationToken);
}
