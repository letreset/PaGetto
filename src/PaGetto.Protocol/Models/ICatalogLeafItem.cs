using System;

namespace PaGetto.Protocol.Models;

/// <summary>
/// A catalog leaf. Represents a single package event.<br/>
/// Leafs can be discovered from a <see cref="CatalogPage"/>.
/// </summary>
/// <remarks>
/// See: <see href="https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-leaf"/>
/// </remarks>
public interface ICatalogLeafItem
{
    /// <summary>
    /// The commit timestamp of this catalog item.
    /// </summary>
    DateTimeOffset CommitTimestamp { get; }

    /// <summary>
    /// The package ID of the catalog item.
    /// </summary>
    string PackageId { get; }

    /// <summary>
    /// The package version of the catalog item.
    /// </summary>
    string PackageVersion { get; }
}
