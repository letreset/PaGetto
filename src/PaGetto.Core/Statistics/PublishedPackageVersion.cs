using System;

namespace PaGetto.Core.Statistics;

/// <summary>
/// A package version and when it was published.
/// </summary>
public class PublishedPackageVersion
{
    public string Id { get; init; }
    public string Version { get; init; }
    public DateTime Published { get; init; }
}
