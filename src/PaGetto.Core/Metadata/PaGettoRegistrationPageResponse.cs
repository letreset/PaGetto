using System.Collections.Generic;
using System.Text.Json.Serialization;
using PaGetto.Protocol.Models;

namespace PaGetto.Core.Metadata;

/// <summary>
/// PaGetto's extensions to a registration page response.
/// </summary>
/// <remarks>Extends <see cref="RegistrationPageResponse"/>.</remarks>
public class PaGettoRegistrationPageResponse : RegistrationPageResponse
{
    /// <summary>
    /// The registration leafs in this page.
    /// </summary>
    /// <remarks>This was modified to use PaGetto's extended registration index page item model.</remarks>
    [JsonPropertyName("items")]
    [JsonPropertyOrder(int.MaxValue)]
    public new IReadOnlyList<PaGettoRegistrationIndexPageItem> ItemsOrNull { get; set; }
}
