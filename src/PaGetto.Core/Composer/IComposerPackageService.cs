using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Composer;

/// <summary>
/// The packages of a Composer feed (<see cref="FeedType.Composer"/>): those uploaded to it, combined
/// with those of its mirrors. Names are matched regardless of case, versions by their normalized form.
/// </summary>
public interface IComposerPackageService
{
    /// <summary>
    /// Gets the <c>p2/</c> metadata of a package: its tagged versions, or its development versions
    /// (the <c>~dev</c> file). A version of the feed hides the mirror's version of the same normalized
    /// form, and an unlisted version is left out. The tagged versions of the mirrors are served
    /// through the feed, with their dist pointing at it; their development versions are passed
    /// through as the mirror lists them.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The package name.</param>
    /// <param name="dev">Whether to list the development versions rather than the tagged ones.</param>
    /// <param name="distBaseUrl">The absolute URL the paths of the feed's zips are under (see <see cref="ComposerManifest.GetDistPath"/>).</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The metadata as JSON, or null if neither the feed nor its mirrors have such versions of the package.</returns>
    Task<string> GetMetadataOrNullAsync(Feed feed, string name, bool dev, string distBaseUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the zip of a version and counts the download. A tagged version that only a mirror has
    /// is downloaded from it and stored in the feed first.
    /// </summary>
    /// <returns>The zip, or null if the version does not exist.</returns>
    Task<Stream> GetDistStreamOrNullAsync(Feed feed, string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Uploads the zip of a version to the feed.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The package name.</param>
    /// <param name="version">The version, which <c>composer.json</c> usually doesn't have.</param>
    /// <param name="zip">The package zip, read to its end.</param>
    /// <param name="maxBytes">The largest zip the feed accepts.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<ComposerUploadResult> UploadAsync(Feed feed, string name, string version, Stream zip, long maxBytes, CancellationToken cancellationToken);
}
