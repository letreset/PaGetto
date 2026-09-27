using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PaGetto.Protocol.Models;

/// <summary>
/// A catalog page, used to discover catalog leafs.<br/>
/// Pages can be discovered from a <see cref="CatalogIndex"/>.
/// </summary>
/// <remarks>
/// See: <see href="https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-page"/>
/// </remarks>
public class CatalogPage
{
    /// <summary>
    /// A unique ID associated with the most recent commit in this page.
    /// </summary>
    [JsonPropertyName("commitTimeStamp")]
    public DateTimeOffset CommitTimestamp { get; set; }

    /// <summary>
    /// The number of items in the page.
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>
    /// The items used to discover <see cref="CatalogLeaf"/>s.
    /// </summary>
    [JsonPropertyName("items")]
    public List<CatalogLeafItem> Items { get; set; }

    /// <summary>
    /// The URL to the Catalog Index.
    /// </summary>
    [JsonPropertyName("parent")]
    public string CatalogIndexUrl { get; set; }
}
