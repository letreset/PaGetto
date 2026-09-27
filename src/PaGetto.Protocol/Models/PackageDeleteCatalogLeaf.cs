namespace PaGetto.Protocol.Models;

/// <summary>
/// A "package delete" catalog leaf. Represents a single package deletion event.<br/>
/// Leafs can be discovered from a <see cref="CatalogPage"/>.
/// </summary>
/// <remarks>
/// See: <see href="https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-leaf"/>
/// </remarks>    
public class PackageDeleteCatalogLeaf : CatalogLeaf
{
}
