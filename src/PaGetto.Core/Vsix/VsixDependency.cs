using System.Text.Json.Serialization;

namespace PaGetto.Core.Vsix;

/// <summary>
/// Another extension an extension needs: a <c>Dependency</c> of its manifest.
/// </summary>
public sealed class VsixDependency
{
    /// <summary>
    /// The identifier of the extension.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; }

    /// <summary>
    /// The versions that satisfy it, as an interval, e.g. <c>[1.0,)</c>, or null.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; init; }
}
