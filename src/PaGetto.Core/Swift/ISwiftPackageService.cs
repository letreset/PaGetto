using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Swift;

/// <summary>
/// The packages of a Swift feed (<see cref="FeedType.Swift"/>): those published to it, combined with
/// those of its mirrors. A package is an <see cref="Artifact"/> under its lowercased identifier
/// <c>scope.name</c>, a release's source archive is the version's archive, and its manifest is a
/// <see cref="SwiftManifest"/>. An unlisted release is left out of the release list, but can still
/// be downloaded.
/// </summary>
public interface ISwiftPackageService
{
    /// <summary>
    /// Lists the releases of a package, those of the feed and those of its mirrors. A release of the
    /// feed, listed or not, hides the mirror's release of the same version.
    /// </summary>
    /// <returns>The releases, or null if neither the feed nor its mirrors have the package.</returns>
    Task<SwiftReleaseList> ListReleasesOrNullAsync(Feed feed, string scope, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a release. A release that only a mirror has is downloaded from it and stored in the
    /// feed first.
    /// </summary>
    /// <returns>The release, or null if neither the feed nor its mirrors have it.</returns>
    Task<SwiftRelease> GetReleaseOrNullAsync(Feed feed, string scope, string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the source archive of a release found by <see cref="GetReleaseOrNullAsync"/>, and counts the download.
    /// </summary>
    Task<Stream> GetArchiveStreamAsync(Feed feed, SwiftRelease release, CancellationToken cancellationToken);

    /// <summary>
    /// The identifiers of the packages whose metadata names a repository URL, in the feed and on its
    /// mirrors.
    /// </summary>
    /// <returns>The identifiers, or an empty list.</returns>
    Task<IReadOnlyList<string>> LookupIdentifiersAsync(Feed feed, string repositoryUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a release.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="scope">The package's scope.</param>
    /// <param name="name">The package's name.</param>
    /// <param name="version">The version.</param>
    /// <param name="archive">The source archive, read to its end.</param>
    /// <param name="metadata">The release metadata as JSON, or null.</param>
    /// <param name="signature">The signature of the source archive, base64-encoded, or null.</param>
    /// <param name="signatureFormat">The format of the signature, or null.</param>
    /// <param name="maxBytes">The largest archive the feed accepts.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<SwiftPublishResult> PublishAsync(
        Feed feed,
        string scope,
        string name,
        string version,
        Stream archive,
        string metadata,
        string signature,
        string signatureFormat,
        long maxBytes,
        CancellationToken cancellationToken);
}
