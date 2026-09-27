using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PaGetto.Protocol.Models;

/// <summary>
/// The catalog index is the entry point for the catalog resource.<br/>
/// Use this to discover catalog pages, which in turn can be used to discover catalog leafs.
/// </summary>
/// <remarks>
/// See: <see href="https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-index"/>
/// </remarks>
public class CatalogIndex
{
    /// <summary>
    /// A timestamp of the most recent commit.
    /// </summary>
    [JsonPropertyName("commitTimeStamp")]
    public DateTimeOffset CommitTimestamp { get; set; }

    /// <summary>
    /// The number of catalog pages in the catalog index.
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>
    /// The items used to discover <see cref="CatalogPage"/>s.
    /// </summary>
    [JsonPropertyName("items")]
    public List<CatalogPageItem> Items { get; set; }
}
