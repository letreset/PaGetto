using System.IO;
using PaGetto.Core.Entities;

namespace PaGetto.Core.PyPI;

/// <summary>
/// A stored file of a version, as <see cref="IPyPIPackageService.DownloadOrNullAsync"/> returns it.
/// </summary>
public sealed class PyPIDownload
{
    public Stream Stream { get; init; }

    public ArtifactFile File { get; init; }
}
