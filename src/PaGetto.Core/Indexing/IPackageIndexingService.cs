using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PaGetto.Core.Indexing;

/// <summary>
/// The result of attempting to index a package.
/// See <see cref="IPackageIndexingService.IndexAsync(Guid, string, Stream, string, DateTime?, CancellationToken)"/>.
/// </summary>
public enum PackageIndexingResult
{
    /// <summary>
    /// The package is malformed. This may also happen if PaGetto is in a corrupted state.
    /// </summary>
    InvalidPackage,

    /// <summary>
    /// The package has already been indexed.
    /// </summary>
    PackageAlreadyExists,

    /// <summary>
    /// The package has been indexed successfully.
    /// </summary>
    Success,
}

/// <summary>
/// The service used to accept new packages.
/// </summary>
public interface IPackageIndexingService
{
    /// <summary>
    /// Attempt to index a new package.
    /// </summary>
    /// <param name="feedId">The feed's id.</param>
    /// <param name="feedSlug">The feed's slug, used to prefix storage paths.</param>
    /// <param name="packageStream">The stream containing the package's content.</param>
    /// <param name="cacheFeedUrl">The upstream feed the package was mirrored from, or null for a push.</param>
    /// <param name="published">The upstream publish date of a mirrored package, or null to use the current time.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The result of the attempted indexing operation.</returns>
    Task<PackageIndexingResult> IndexAsync(Guid feedId, string feedSlug, Stream packageStream, string cacheFeedUrl, DateTime? published, CancellationToken cancellationToken);
}
