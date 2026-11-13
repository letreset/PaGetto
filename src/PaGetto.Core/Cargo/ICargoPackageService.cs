using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Cargo;

/// <summary>
/// The crates of a Cargo feed (<see cref="FeedType.Cargo"/>): those published to it, combined with
/// those of its mirrors. Crate names are matched regardless of case.
/// </summary>
public interface ICargoPackageService
{
    /// <summary>
    /// Finds the versions of a crate's index file. A version published to the feed hides the same
    /// version of a mirror.
    /// </summary>
    /// <returns>The versions, oldest first. Empty if neither the feed nor its mirrors have the crate.</returns>
    Task<IReadOnlyList<CargoIndexVersion>> FindVersionsAsync(Feed feed, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a version's <c>.crate</c> file and counts the download. A version that only a mirror
    /// has is downloaded from it and stored in the feed first.
    /// </summary>
    /// <returns>The crate, or null if the version does not exist.</returns>
    Task<Stream> GetCrateStreamOrNullAsync(Feed feed, string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a crate to the feed, as <c>cargo publish</c> sends it.
    /// </summary>
    Task<CargoPublishResult> PublishAsync(Feed feed, CargoPublishRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Yanks or unyanks a version published to the feed or stored from a mirror.
    /// </summary>
    /// <returns>False if the feed has no such version.</returns>
    Task<bool> SetYankedAsync(Feed feed, string name, string version, bool yanked, CancellationToken cancellationToken);
}
