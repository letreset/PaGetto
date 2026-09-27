using System.Text.Json.Serialization;
using PaGetto.Protocol.Models;

namespace PaGetto.Core.Metadata;

/// <summary>
/// PaGetto's extensions to a registration index page.
/// </summary>
/// <remarks>Extends <see cref="RegistrationIndexPageItem"/>.</remarks>
public class PaGettoRegistrationIndexPageItem : RegistrationIndexPageItem
{
    /// <summary>
    /// The catalog entry containing the package metadata.
    /// </summary>
    /// <remarks>This was modified to use PaGetto's extended package metadata model.</remarks>
    [JsonPropertyName("catalogEntry")]
    [JsonPropertyOrder(int.MaxValue)]
    public new PaGettoPackageMetadata PackageMetadata { get; set; }
}
