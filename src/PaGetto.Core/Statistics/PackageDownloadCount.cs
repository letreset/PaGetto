namespace PaGetto.Core.Statistics;

/// <summary>
/// The downloads of all versions of a package.
/// </summary>
public class PackageDownloadCount
{
    public string Id { get; init; }
    public long Downloads { get; init; }
}
