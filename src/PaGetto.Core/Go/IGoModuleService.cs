using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Go;

/// <summary>
/// The modules of a Go feed (<see cref="FeedType.Go"/>): those uploaded to it, combined with those
/// of its mirrors. Module paths and versions are the unescaped, case-sensitive ones.
/// </summary>
public interface IGoModuleService
{
    /// <summary>
    /// Lists the listed versions of a module, without pseudo-versions (its <c>@v/list</c>). A
    /// version unlisted in the feed is left out even when a mirror lists it.
    /// </summary>
    /// <returns>The versions, oldest first, or null if neither the feed nor its mirrors have the module.</returns>
    Task<IReadOnlyList<string>> ListVersionsOrNullAsync(Feed feed, string modulePath, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the latest version of a module (its <c>@latest</c>): the newest release, or the newest
    /// prerelease when there is none, or the newest pseudo-version when there is no tagged version.
    /// </summary>
    /// <returns>The version's <c>.info</c> JSON, or null if the module has no listed version.</returns>
    Task<string> GetLatestInfoOrNullAsync(Feed feed, string modulePath, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the <c>.info</c> of a version. A query that is not a canonical version, such as a
    /// branch name, is passed on to the mirrors, which may resolve it.
    /// </summary>
    /// <returns>The <c>.info</c> JSON, or null if the version does not exist.</returns>
    Task<string> GetInfoOrNullAsync(Feed feed, string modulePath, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the <c>go.mod</c> file of a version.
    /// </summary>
    /// <returns>The file's content, or null if the version does not exist.</returns>
    Task<string> GetModOrNullAsync(Feed feed, string modulePath, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the zip of a version and counts the download. A version that only a mirror has is
    /// downloaded from it and stored in the feed first.
    /// </summary>
    /// <returns>The zip, or null if the version does not exist.</returns>
    Task<Stream> GetZipStreamOrNullAsync(Feed feed, string modulePath, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Uploads the zip of a version to the feed.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="modulePath">The module path.</param>
    /// <param name="version">The version.</param>
    /// <param name="zip">The module zip, read to its end.</param>
    /// <param name="maxBytes">The largest zip the feed accepts.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<GoUploadResult> UploadAsync(Feed feed, string modulePath, string version, Stream zip, long maxBytes, CancellationToken cancellationToken);
}
