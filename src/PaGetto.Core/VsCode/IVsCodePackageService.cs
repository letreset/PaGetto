using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.VsCode;

/// <summary>
/// The VS Code extensions of a VS Code feed (<see cref="FeedType.VsCode"/>): those uploaded to it,
/// combined with those of its mirrors, served through the gallery API. Identifiers are matched
/// regardless of case. An extension that has a version uploaded to the feed is never looked up on
/// the mirrors, so a mirror can't offer versions of the feed's own extensions.
/// </summary>
public interface IVsCodePackageService
{
    /// <summary>
    /// Answers a query of the gallery API: the extensions it names, or a page of the extensions
    /// that match its search, the feed's first and then the mirror's. The URLs of the answer are on
    /// this server, so the editor needs no access to the mirrors.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="query">The query.</param>
    /// <param name="galleryUrl">The absolute URL of the feed's gallery, without a trailing slash.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<VsCodeGalleryQueryResult> QueryAsync(Feed feed, VsCodeGalleryQuery query, string galleryUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Finds an extension with its latest stable and latest pre-release version for each platform.
    /// </summary>
    /// <returns>The extension, or null if neither the feed nor a mirror has it.</returns>
    Task<VsCodeGalleryExtension> GetLatestOrNullAsync(Feed feed, string id, string galleryUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a file of a version for a platform: its VSIX file, which counts a download, or one of
    /// the assets its manifest lists. A VSIX file that only a mirror has is downloaded from it and
    /// stored in the feed first; the other assets of such a version are passed through.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="id">The extension's identifier, <c>publisher.name</c>.</param>
    /// <param name="version">The version.</param>
    /// <param name="targetPlatform">The platform, or null for the universal file.</param>
    /// <param name="assetType">The asset type, such as <see cref="VsCodeGalleryApi.VsixPackageAsset"/>.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The file, or null if the version or the asset does not exist.</returns>
    Task<VsCodeAsset> GetAssetOrNullAsync(Feed feed, string id, string version, string targetPlatform, string assetType, CancellationToken cancellationToken);

    /// <summary>
    /// Uploads a VSIX file to the feed. Its identifier, version and platform are read from its manifest.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="vsix">The file, read to its end.</param>
    /// <param name="maxBytes">The largest file the feed accepts.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<VsCodeUploadResult> UploadAsync(Feed feed, Stream vsix, long maxBytes, CancellationToken cancellationToken);
}
