using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Npm;

/// <summary>
/// The packages of an npm feed (<see cref="FeedType.Npm"/>): those published to it, combined with
/// those of its mirrors. Package names are passed as clients send them, e.g. <c>@scope/name</c>.
/// </summary>
public interface INpmPackageService
{
    /// <summary>
    /// Builds a package's document (the "packument"): every version's manifest, the dist-tags and
    /// the publish times. A version published to the feed hides the same version of a mirror, and
    /// so does a tag.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The package name.</param>
    /// <param name="localOnly">
    /// Leave out what only the mirrors have. Clients that change a package (deprecate, unpublish)
    /// read it this way, and send back what they changed.
    /// </param>
    /// <param name="getTarballUrl">The URL of a version's tarball, given the name and version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The document, or null if neither the feed nor its mirrors have the package.</returns>
    Task<JsonObject> GetPackumentOrNullAsync(
        Feed feed,
        string name,
        bool localOnly,
        Func<string, string, string> getTarballUrl,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds the dist-tags of a package, as its document lists them.
    /// </summary>
    /// <returns>The tags, or null if neither the feed nor its mirrors have the package.</returns>
    Task<IReadOnlyDictionary<string, string>> GetDistTagsOrNullAsync(Feed feed, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a version's tarball and counts the download. A version that only a mirror has is
    /// downloaded from it and stored in the feed first.
    /// </summary>
    /// <returns>The tarball, or null if the version does not exist.</returns>
    Task<Stream> GetTarballStreamOrNullAsync(Feed feed, string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a version, as <c>npm publish</c> sends it: one manifest in <c>versions</c>, its
    /// tarball in <c>_attachments</c> and the tags to point at it in <c>dist-tags</c>.
    /// </summary>
    /// <param name="feed">The feed to publish to.</param>
    /// <param name="name">The package name from the URL.</param>
    /// <param name="body">The request body.</param>
    /// <param name="maxTarballBytes">The largest tarball the feed accepts.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<NpmResult> PublishAsync(Feed feed, string name, JsonElement body, long maxTarballBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a changed package document, as <c>npm deprecate</c> and <c>npm unpublish</c> send
    /// it: versions of the feed that are missing from it are removed, and the deprecation message
    /// of each version is applied. A deprecated version is unlisted.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The package name from the URL.</param>
    /// <param name="body">The changed document.</param>
    /// <param name="canDeprecate">Whether the caller may deprecate and undeprecate versions.</param>
    /// <param name="canUnpublish">Whether the caller may remove versions.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<NpmResult> UpdateAsync(
        Feed feed,
        string name,
        JsonElement body,
        bool canDeprecate,
        bool canUnpublish,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes one version of a package from the feed, or all of them.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The package name.</param>
    /// <param name="version">The version, or null for every version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<NpmResult> UnpublishAsync(Feed feed, string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Points a dist-tag at a version published to or stored in the feed.
    /// </summary>
    Task<NpmResult> SetDistTagAsync(Feed feed, string name, string tag, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a dist-tag. The <c>latest</c> tag can't be removed.
    /// </summary>
    Task<NpmResult> RemoveDistTagAsync(Feed feed, string name, string tag, CancellationToken cancellationToken);
}
