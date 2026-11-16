using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Hex;

/// <summary>
/// The packages of a Hex feed (<see cref="FeedType.Hex"/>): those published to it, combined with
/// those of its mirrors. The feed is a Hex repository named after its slug.
/// </summary>
public interface IHexPackageService
{
    /// <summary>
    /// Finds the versions of a package for its registry resource. A version published to the feed
    /// hides the same version of a mirror.
    /// </summary>
    /// <returns>The versions, oldest first. Empty if neither the feed nor its mirrors have the package.</returns>
    Task<IReadOnlyList<HexRelease>> FindReleasesAsync(Feed feed, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the packages stored in the feed, published ones and mirrored ones once downloaded,
    /// for the registry's <c>/names</c> and <c>/versions</c>.
    /// </summary>
    /// <returns>The packages by name, in order, each with its versions oldest first.</returns>
    Task<IReadOnlyList<KeyValuePair<string, IReadOnlyList<HexRelease>>>> FindPackagesAsync(Feed feed, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a version's tarball and counts the download. A version that only a mirror has is
    /// downloaded from it and stored in the feed first.
    /// </summary>
    /// <returns>The tarball, or null if the version does not exist.</returns>
    Task<Stream> GetTarballStreamOrNullAsync(Feed feed, string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a package tarball to the feed, as <c>mix hex.publish</c> sends it.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="tarball">The tarball, e.g. the request body. It is read to its end.</param>
    /// <param name="maxBytes">The largest tarball accepted.</param>
    /// <param name="replace">Whether the client asked to replace an existing version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<HexPublishResult> PublishAsync(Feed feed, Stream tarball, long maxBytes, bool replace, CancellationToken cancellationToken);

    /// <summary>
    /// Retires or unretires a version published to the feed or stored from a mirror.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The package's name.</param>
    /// <param name="version">The version.</param>
    /// <param name="retirement">Why the version is retired, or null to unretire it.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>False if the feed has no such version.</returns>
    Task<bool> SetRetirementAsync(Feed feed, string name, string version, HexRetirement retirement, CancellationToken cancellationToken);
}
