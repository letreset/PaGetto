using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PaGetto.Core.Artifacts;

/// <summary>
/// Stores the content of the packages of feeds that are not NuGet feeds. Their state is stored by
/// the <see cref="IArtifactDatabase"/>.
/// </summary>
public interface IArtifactStorageService
{
    /// <summary>
    /// Persists a version's content. Fails if the version is already stored with different content.
    /// </summary>
    /// <param name="feedSlug">The feed slug used to prefix storage paths.</param>
    /// <param name="name">The package's normalized name.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="archive">The archive as its ecosystem's clients download it.</param>
    /// <param name="readme">The readme, or null if the package has none.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task SaveAsync(string feedSlug, string name, string version, Stream archive, Stream readme, CancellationToken cancellationToken);

    Task<Stream> GetArchiveStreamAsync(string feedSlug, string name, string version, CancellationToken cancellationToken);

    Task<Stream> GetReadmeStreamAsync(string feedSlug, string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a version's content. Succeeds even if nothing is stored.
    /// </summary>
    Task DeleteAsync(string feedSlug, string name, string version, CancellationToken cancellationToken);
}
